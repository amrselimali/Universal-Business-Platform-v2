using Dapper;
using Npgsql;
using Ubp.Core.Models;
using Ubp.Core.Results;
using Ubp.Infrastructure.Data;

namespace Ubp.Infrastructure.Repositories;

public interface IPosRepository
{
    Task<PosReceipt> CheckoutAsync(PosCheckoutInput input, Guid userId,
        CancellationToken ct = default);
    Task<IReadOnlyList<CommercialDocument>> ListReceiptsAsync(Guid? branchId, int limit,
        CancellationToken ct = default);
}

/// <summary>
/// نقطة البيع (4G): حجز فوري + تنفيذ + فاتورة + سند قبض — كلها في معاملة واحدة،
/// وأي فشل يترك القاعدة بلا أي أثر.
/// </summary>
public sealed partial class PosRepository : IPosRepository
{
    private readonly IDbConnectionFactory _factory;
    public PosRepository(IDbConnectionFactory factory) => _factory = factory;

    private sealed record EntryLine(Guid AccountId, decimal Debit, decimal Credit,
        Guid? CustomerId = null);

    private sealed class BuiltLine
    {
        public int LineNo { get; set; }
        public Guid ItemId { get; set; }
        public Guid UnitId { get; set; }
        public Guid? TaxId { get; set; }
        public decimal Quantity { get; set; }
        public decimal UnitPrice { get; set; }
        public decimal DiscountPercent { get; set; }
        public decimal NetAmount { get; set; }
        public decimal TaxAmount { get; set; }
        public decimal TotalAmount { get; set; }
    }

    public async Task<PosReceipt> CheckoutAsync(PosCheckoutInput input, Guid userId,
        CancellationToken ct = default)
    {
        if (input.BranchId == Guid.Empty || input.CustomerId == Guid.Empty ||
            input.WarehouseId == Guid.Empty || input.TreasuryId == Guid.Empty ||
            input.TreasuryMethodId == Guid.Empty)
            throw new ValidationAppException("validation.required_fields");
        if (input.Lines.Count == 0)
            throw new ValidationAppException("validation.document_has_no_lines");

        await using var connection = await _factory.OpenAsync(ct);
        await using var tx = await connection.BeginTransactionAsync(ct);

        var treasury = await connection.QuerySingleOrDefaultAsync<(Guid AccountId,
            Guid? ClearingAccountId, Guid BranchId, string TreasuryType)>(new CommandDefinition("""
            SELECT t.account_id, m.clearing_account_id, t.branch_id, t.treasury_type
            FROM treasury_methods m JOIN treasuries t ON t.id = m.treasury_id
            WHERE m.id = @methodId AND m.treasury_id = @treasuryId AND m.is_active AND t.is_active
            """, new { methodId = input.TreasuryMethodId, treasuryId = input.TreasuryId }, tx,
            cancellationToken: ct));
        if (treasury.AccountId == Guid.Empty)
            throw new ValidationAppException("validation.treasury_method_not_found");
        if (treasury.BranchId != input.BranchId)
            throw new ValidationAppException("validation.treasury_branch_mismatch");

        var rates = await LoadTaxRatesAsync(connection, tx, input, ct);
        var lines = BuildLines(input, rates);
        var total = lines.Sum(l => l.TotalAmount);

        var bookingId = Guid.CreateVersion7();
        var bookingNo = await NextNumberAsync(connection, tx, "BOOK", input.BranchId,
            input.DocDate, "BK", ct);
        await connection.ExecuteAsync(new CommandDefinition("""
            INSERT INTO bookings (id, branch_id, doc_no, booking_date, status, customer_id, source,
                due_date, total_amount, notes, created_by)
            VALUES (@bookingId, @BranchId, @bookingNo, @docDate, 'confirmed', @CustomerId, 'pos',
                    @docDate, @total, @Notes, @userId)
            """, new { bookingId, input.BranchId, docDate = input.DocDate.Date, bookingNo,
                input.CustomerId, total, input.Notes, userId }, tx, cancellationToken: ct));

        var bookingLines = new List<(Guid Id, BuiltLine Line)>();
        foreach (var line in lines)
        {
            var lineId = await connection.ExecuteScalarAsync<Guid>(new CommandDefinition("""
                INSERT INTO booking_lines (booking_id, line_no, item_id, unit_id, warehouse_id,
                    quantity, unit_price, discount_percent, tax_id, net_amount, tax_amount,
                    total_amount, delivered_qty, invoiced_qty)
                VALUES (@bookingId, @LineNo, @ItemId, @UnitId, @WarehouseId, @Quantity, @UnitPrice,
                        @DiscountPercent, @TaxId, @NetAmount, @TaxAmount, @TotalAmount, @Quantity,
                        @Quantity)
                RETURNING id
                """, new { bookingId, line.LineNo, line.ItemId, line.UnitId, input.WarehouseId,
                    line.Quantity, line.UnitPrice, line.DiscountPercent, line.TaxId,
                    line.NetAmount, line.TaxAmount, line.TotalAmount }, tx, cancellationToken: ct));
            bookingLines.Add((lineId, line));
        }

        var deliveryId = Guid.CreateVersion7();
        await connection.ExecuteAsync(new CommandDefinition("""
            INSERT INTO delivery_notes (id, branch_id, doc_date, status, booking_id, warehouse_id,
                notes, created_by)
            VALUES (@deliveryId, @BranchId, @docDate, 'draft', @bookingId, @WarehouseId, @Notes,
                    @userId)
            """, new { deliveryId, input.BranchId, docDate = input.DocDate.Date, bookingId,
                input.WarehouseId, input.Notes, userId }, tx, cancellationToken: ct));

        var lineNo = 0;
        foreach (var (lineId, line) in bookingLines)
        {
            lineNo++;
            await connection.ExecuteAsync(new CommandDefinition("""
                INSERT INTO delivery_note_lines (delivery_note_id, line_no, booking_line_id, item_id,
                    unit_id, quantity)
                VALUES (@deliveryId, @lineNo, @bookingLineId, @ItemId, @UnitId, @Quantity)
                """, new { deliveryId, lineNo, bookingLineId = lineId, line.ItemId, line.UnitId,
                    line.Quantity }, tx, cancellationToken: ct));
        }

        var stockDocId = await CreateStockDocAsync(connection, tx, deliveryId, input, bookingLines,
            userId, ct);

        var costs = (await connection.QueryAsync<(int LineNo, decimal TotalCost)>(
            new CommandDefinition(
                "SELECT line_no, total_cost FROM stock_document_lines WHERE document_id = @stockDocId",
                new { stockDocId }, tx, cancellationToken: ct))).ToDictionary(x => x.LineNo,
            x => x.TotalCost);

        decimal costTotal = 0;
        foreach (var (lineId, line) in bookingLines)
        {
            var cost = Math.Round(costs.GetValueOrDefault(line.LineNo), 2,
                MidpointRounding.AwayFromZero);
            costTotal += cost;
            await connection.ExecuteAsync(new CommandDefinition("""
                UPDATE delivery_note_lines SET unit_cost = @unitCost, cost_total = @cost
                WHERE delivery_note_id = @deliveryId AND booking_line_id = @bookingLineId
                """, new { unitCost = line.Quantity == 0 ? 0 : Math.Round(cost / line.Quantity, 6),
                    cost, deliveryId, bookingLineId = lineId }, tx, cancellationToken: ct));
            await connection.ExecuteAsync(new CommandDefinition(
                "UPDATE booking_lines SET reserved_qty = 0 WHERE id = @id", new { id = lineId },
                tx, cancellationToken: ct));
        }

        var deliveryNo = await NextNumberAsync(connection, tx, "DELIVERY", input.BranchId,
            input.DocDate, "DN", ct);
        var cogsEntry = costTotal > 0
            ? await PostEntryAsync(connection, tx, "stock", input.BranchId, input.DocDate, deliveryNo,
                $"تكلفة POS {deliveryNo}", new[]
                {
                    new EntryLine(await NeedAsync(connection, tx, PostingKeys.StockCogs, ct), costTotal, 0),
                    new EntryLine(await NeedAsync(connection, tx, PostingKeys.StockInventory, ct), 0, costTotal)
                }, userId, "delivery", deliveryId, ct)
            : (Guid?)null;

        await connection.ExecuteAsync(new CommandDefinition("""
            UPDATE delivery_notes SET status = 'posted', doc_no = @deliveryNo, posted_at = now(),
                posted_by = @userId, journal_entry_id = @cogsEntry, stock_document_id = @stockDocId,
                updated_by = @userId
            WHERE id = @deliveryId
            """, new { deliveryNo, userId, cogsEntry, stockDocId, deliveryId }, tx,
            cancellationToken: ct));

        await connection.ExecuteAsync(new CommandDefinition(
            "UPDATE bookings SET status = 'completed' WHERE id = @bookingId",
            new { bookingId }, tx, cancellationToken: ct));

        var invoiceId = await SalesInvoiceRepository.CreateFromDeliveryAsync(connection, tx,
            deliveryId, userId, ct) ?? throw new ValidationAppException("validation.pos_invoice_failed");

        var invoice = await connection.QuerySingleAsync<(Guid Id, string? DocNo, decimal Total)>(
            new CommandDefinition(
                "SELECT id, doc_no, total FROM commercial_documents WHERE id = @invoiceId",
                new { invoiceId }, tx, cancellationToken: ct));

        var voucherId = Guid.CreateVersion7();
        var voucherNo = await NextNumberAsync(connection, tx, "RCV", input.BranchId, input.DocDate,
            "RCV", ct);
        await connection.ExecuteAsync(new CommandDefinition("""
            INSERT INTO vouchers (id, branch_id, doc_date, status, voucher_type, party_type,
                party_id, party_name, treasury_id, total_amount, fee_total, created_by)
            VALUES (@voucherId, @BranchId, @docDate, 'draft', 'receipt', 'C', @CustomerId,
                    (SELECT name_ar FROM customers WHERE id = @CustomerId), @TreasuryId, @total,
                    0, @userId)
            """, new { voucherId, input.BranchId, docDate = input.DocDate.Date, input.CustomerId,
                input.TreasuryId, total, userId }, tx, cancellationToken: ct));
        await connection.ExecuteAsync(new CommandDefinition("""
            INSERT INTO voucher_lines (voucher_id, line_no, treasury_method_id, amount, fee_amount)
            VALUES (@voucherId, 1, @methodId, @total, 0)
            """, new { voucherId, methodId = input.TreasuryMethodId, total }, tx,
            cancellationToken: ct));

        var advances = await NeedAsync(connection, tx, PostingKeys.CustomerAdvances, ct);
        var cashAccount = treasury.ClearingAccountId ?? treasury.AccountId;
        var voucherEntry = await PostEntryAsync(connection, tx,
            treasury.TreasuryType == TreasuryTypes.Cash ? "cash" : "bank", input.BranchId,
            input.DocDate, voucherNo, $"قبض POS {voucherNo}", new[]
            {
                new EntryLine(cashAccount, total, 0),
                new EntryLine(advances, 0, total, input.CustomerId)
            }, userId, "voucher", voucherId, ct);

        await connection.ExecuteAsync(new CommandDefinition("""
            UPDATE vouchers SET status = 'posted', doc_no = @voucherNo, posted_at = now(),
                posted_by = @userId, journal_entry_id = @voucherEntry WHERE id = @voucherId
            """, new { voucherNo, userId, voucherEntry, voucherId }, tx, cancellationToken: ct));

        var allocation = Math.Min(total, invoice.Total);
        if (allocation > 0)
        {
            await connection.ExecuteAsync(new CommandDefinition("""
                INSERT INTO document_allocations (id, source_voucher_id, target_document_id, amount,
                    created_by)
                VALUES (@id, @voucherId, @invoiceId, @allocation, @userId)
                """, new { id = Guid.CreateVersion7(), voucherId, invoiceId, allocation, userId }, tx,
                cancellationToken: ct));
            await connection.ExecuteAsync(new CommandDefinition(
                "UPDATE commercial_documents SET allocated_total = allocated_total + @allocation " +
                "WHERE id = @invoiceId", new { allocation, invoiceId }, tx, cancellationToken: ct));

            await PostEntryAsync(connection, tx, "general", input.BranchId, input.DocDate, voucherNo,
                $"مقاصة POS {voucherNo}", new[]
                {
                    new EntryLine(advances, allocation, 0, input.CustomerId),
                    new EntryLine(await NeedAsync(connection, tx, PostingKeys.CustomerReceivable, ct),
                        0, allocation, input.CustomerId)
                }, userId, "voucher", null, ct);
        }

        await tx.CommitAsync(ct);
        return new PosReceipt
        {
            BookingId = bookingId, BookingNo = bookingNo, DeliveryId = deliveryId,
            DeliveryNo = deliveryNo, InvoiceId = invoiceId, InvoiceNo = invoice.DocNo,
            VoucherId = voucherId, VoucherNo = voucherNo, Total = total
        };
    }

    public async Task<IReadOnlyList<CommercialDocument>> ListReceiptsAsync(Guid? branchId,
        int limit, CancellationToken ct = default)
    {
        await using var connection = await _factory.OpenAsync(ct);
        return (await connection.QueryAsync<CommercialDocument>(new CommandDefinition("""
            SELECT d.id AS Id, d.doc_type AS DocType, d.doc_no AS DocNo, d.doc_date AS DocDate,
                   d.status AS Status, d.branch_id AS BranchId, d.party_name AS PartyName,
                   d.total AS Total, d.allocated_total AS AllocatedTotal,
                   d.journal_entry_id AS JournalEntryId
            FROM commercial_documents d
            WHERE d.doc_type = 'sales_invoice' AND d.booking_id IS NOT NULL
              AND EXISTS (SELECT 1 FROM bookings b WHERE b.id = d.booking_id AND b.source = 'pos')
              AND (@branchId::uuid IS NULL OR d.branch_id = @branchId::uuid)
            ORDER BY d.doc_date DESC, d.id DESC LIMIT @limit
            """, new { branchId, limit = Math.Clamp(limit, 1, 200) }, cancellationToken: ct)))
            .ToList();
    }
}
