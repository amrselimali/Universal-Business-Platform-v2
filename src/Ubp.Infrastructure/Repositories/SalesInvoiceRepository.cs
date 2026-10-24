using Dapper;
using Npgsql;
using Ubp.Core.Models;
using Ubp.Core.Results;
using Ubp.Infrastructure.Data;

namespace Ubp.Infrastructure.Repositories;

public interface ISalesInvoiceRepository
{
    Task<Guid> CreateDraftAsync(Guid id, CommercialDocumentInput input, Guid userId,
        CancellationToken ct = default);
    Task<Guid> PostAsync(Guid id, Guid userId, CancellationToken ct = default);
}

/// <summary>
/// فواتير ومرتجعات المبيعات على الجدول الموحد: كل بند مرتبط ببند حجز (D3)،
/// والإيراد عند الفاتورة، والمرتجع يعيد المخزون بتكلفة الأصل (D22).
/// </summary>
public sealed partial class SalesInvoiceRepository : ISalesInvoiceRepository
{
    private readonly IDbConnectionFactory _factory;
    public SalesInvoiceRepository(IDbConnectionFactory factory) => _factory = factory;

    private sealed class BuiltLine
    {
        public int LineNo { get; set; }
        public Guid ItemId { get; set; }
        public Guid UnitId { get; set; }
        public Guid BookingLineId { get; set; }
        public Guid? OriginalLineId { get; set; }
        public decimal Quantity { get; set; }
        public decimal UnitPrice { get; set; }
        public decimal DiscountPercent { get; set; }
        public Guid? TaxId { get; set; }
        public decimal NetAmount { get; set; }
        public decimal TaxAmount { get; set; }
        public decimal TotalAmount { get; set; }
    }

    private sealed class Totals
    {
        public decimal Subtotal { get; set; }
        public decimal DiscountTotal { get; set; }
        public decimal TaxTotal { get; set; }
        public decimal Total { get; set; }
    }

    public async Task<Guid> CreateDraftAsync(Guid id, CommercialDocumentInput input, Guid userId,
        CancellationToken ct = default)
    {
        if (input.DocType is not (CommercialDocTypes.SalesInvoice or CommercialDocTypes.SalesReturn))
            throw new ValidationAppException("validation.not_supported_document");
        if (input.CustomerId is null || input.BranchId == Guid.Empty)
            throw new ValidationAppException("validation.required_fields");
        if (input.Lines.Count == 0)
            throw new ValidationAppException("validation.document_has_no_lines");

        await using var connection = await _factory.OpenAsync(ct);
        await using var tx = await connection.BeginTransactionAsync(ct);

        var isReturn = input.DocType == CommercialDocTypes.SalesReturn;
        var bookingLines = await LoadBookingLinesAsync(connection, tx, input, isReturn, ct);
        var rates = await LoadTaxRatesAsync(connection, tx, input, ct);

        var built = new List<BuiltLine>();
        var lineNo = 0;
        foreach (var line in input.Lines)
        {
            lineNo++;
            if (line.BookingLineId is null)
                throw new ValidationAppException("validation.sales_line_requires_booking");
            if (line.Quantity <= 0)
                throw new ValidationAppException("validation.voucher_amount");
            if (!bookingLines.TryGetValue(line.BookingLineId.Value, out var bookingLine))
                throw new ValidationAppException("validation.booking_line_not_found");

            var allowed = isReturn ? bookingLine.Returnable : bookingLine.Invoiceable;
            if (line.Quantity > allowed)
                throw new ValidationAppException(isReturn
                    ? "validation.return_qty_exceeded" : "validation.invoice_qty_exceeded");

            var gross = line.Quantity * line.UnitPrice;
            var discount = Math.Round(gross * line.DiscountPercent / 100m, 2,
                MidpointRounding.AwayFromZero);
            var net = Math.Round(gross - discount, 2, MidpointRounding.AwayFromZero);
            var rate = line.TaxId is not null && rates.TryGetValue(line.TaxId.Value, out var value)
                ? value : 0m;
            var tax = Math.Round(net * rate / 100m, 2, MidpointRounding.AwayFromZero);

            built.Add(new BuiltLine
            {
                LineNo = lineNo, ItemId = line.ItemId, UnitId = line.UnitId,
                BookingLineId = line.BookingLineId.Value, OriginalLineId = line.OriginalLineId,
                Quantity = line.Quantity, UnitPrice = line.UnitPrice,
                DiscountPercent = line.DiscountPercent, TaxId = line.TaxId, NetAmount = net,
                TaxAmount = tax, TotalAmount = Math.Round(net + tax, 2, MidpointRounding.AwayFromZero)
            });
        }

        if (isReturn)
        {
            foreach (var line in built)
            {
                if (line.OriginalLineId is null)
                    throw new ValidationAppException("validation.original_line_required");
                var belongs = await connection.ExecuteScalarAsync<bool>(new CommandDefinition("""
                    SELECT EXISTS (
                        SELECT 1 FROM commercial_document_lines l
                        JOIN commercial_documents d ON d.id = l.document_id
                        WHERE l.id = @originalId AND d.customer_id = @customerId
                          AND d.doc_type = 'sales_invoice' AND d.status = 'posted')
                    """, new { originalId = line.OriginalLineId, customerId = input.CustomerId },
                    tx, cancellationToken: ct));
                if (!belongs)
                    throw new ValidationAppException("validation.original_line_not_found");
            }
        }

        var totals = new Totals
        {
            Subtotal = built.Sum(l => l.NetAmount),
            DiscountTotal = built.Sum(l => Math.Round(l.Quantity * l.UnitPrice *
                l.DiscountPercent / 100m, 2, MidpointRounding.AwayFromZero)),
            TaxTotal = built.Sum(l => l.TaxAmount),
            Total = built.Sum(l => l.TotalAmount)
        };

        await connection.ExecuteAsync(new CommandDefinition("""
            INSERT INTO commercial_documents (id, branch_id, warehouse_id, doc_date, doc_type,
                customer_id, original_document_id, booking_id, notes, subtotal, discount_total,
                tax_total, total, created_by)
            VALUES (@id, @BranchId, @WarehouseId, @DocDate, @DocType, @CustomerId,
                    @OriginalDocumentId, @BookingId, @Notes, @subtotal, @discount, @tax, @total,
                    @userId)
            """, new
        {
            id, input.BranchId, input.WarehouseId, docDate = input.DocDate.Date, input.DocType,
            input.CustomerId, input.OriginalDocumentId, input.BookingId, input.Notes,
            subtotal = totals.Subtotal, discount = totals.DiscountTotal, tax = totals.TaxTotal,
            total = totals.Total, userId
        }, tx, cancellationToken: ct));

        foreach (var line in built)
        {
            var lineId = Guid.CreateVersion7();
            await connection.ExecuteAsync(new CommandDefinition("""
                INSERT INTO commercial_document_lines (id, document_id, line_no, item_id, unit_id,
                    conversion_factor, quantity, unit_price, discount_percent, discount_amount,
                    net_amount, tax_id, tax_rate, tax_amount, total_amount, booking_line_id)
                VALUES (@lineId, @id, @LineNo, @ItemId, @UnitId, 1, @Quantity, @UnitPrice,
                        @DiscountPercent, @discountAmount, @NetAmount, @TaxId, @taxRate, @TaxAmount,
                        @TotalAmount, @BookingLineId)
                """, new
            {
                lineId, id, line.LineNo, line.ItemId, line.UnitId, line.Quantity, line.UnitPrice,
                line.DiscountPercent, discountAmount = Math.Round(line.Quantity * line.UnitPrice *
                    line.DiscountPercent / 100m, 2, MidpointRounding.AwayFromZero),
                line.NetAmount, line.TaxId, taxRate = line.TaxId is null ? 0m :
                    rates.GetValueOrDefault(line.TaxId.Value), line.TaxAmount, line.TotalAmount,
                line.BookingLineId
            }, tx, cancellationToken: ct));

            if (isReturn)
                await connection.ExecuteAsync(new CommandDefinition("""
                    INSERT INTO sales_return_links (return_line_id, original_line_id, quantity)
                    VALUES (@lineId, @originalId, @quantity)
                    """, new { lineId, originalId = line.OriginalLineId, quantity = line.Quantity },
                    tx, cancellationToken: ct));
        }

        await tx.CommitAsync(ct);
        return id;
    }

    private sealed class BookingLineGuard
    {
        public Guid Id { get; set; }
        public decimal Invoiceable { get; set; }
        public decimal Returnable { get; set; }
    }

    private static async Task<Dictionary<Guid, BookingLineGuard>> LoadBookingLinesAsync(
        NpgsqlConnection connection, NpgsqlTransaction tx, CommercialDocumentInput input,
        bool isReturn, CancellationToken ct)
    {
        var ids = input.Lines.Where(l => l.BookingLineId is not null)
            .Select(l => l.BookingLineId!.Value).Distinct().ToArray();
        if (ids.Length == 0) return new Dictionary<Guid, BookingLineGuard>();

        return (await connection.QueryAsync<BookingLineGuard>(new CommandDefinition("""
            SELECT id AS Id,
                   GREATEST(delivered_qty - invoiced_qty - cancelled_qty, 0) AS Invoiceable,
                   GREATEST(delivered_qty - cancelled_qty, 0) AS Returnable
            FROM booking_lines WHERE id = ANY(@ids)
            """, new { ids }, tx, cancellationToken: ct))).ToDictionary(x => x.Id);
    }

    private static async Task<Dictionary<Guid, decimal>> LoadTaxRatesAsync(
        NpgsqlConnection connection, NpgsqlTransaction tx, CommercialDocumentInput input,
        CancellationToken ct)
    {
        var ids = input.Lines.Where(l => l.TaxId is not null).Select(l => l.TaxId!.Value)
            .Distinct().ToArray();
        if (ids.Length == 0) return new Dictionary<Guid, decimal>();

        return (await connection.QueryAsync<(Guid Id, decimal Rate)>(new CommandDefinition(
            "SELECT id, rate FROM taxes WHERE id = ANY(@ids)", new { ids }, tx,
            cancellationToken: ct))).ToDictionary(x => x.Id, x => x.Rate);
    }
}
