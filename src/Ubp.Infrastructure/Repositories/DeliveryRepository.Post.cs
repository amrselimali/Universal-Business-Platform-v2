using Dapper;
using Npgsql;
using Ubp.Core.Models;
using Ubp.Core.Results;

namespace Ubp.Infrastructure.Repositories;

public sealed partial class DeliveryRepository
{
    private sealed class PostLine
    {
        public Guid Id { get; set; }
        public int LineNo { get; set; }
        public Guid BookingLineId { get; set; }
        public Guid ItemId { get; set; }
        public Guid UnitId { get; set; }
        public decimal Quantity { get; set; }
        public decimal Remaining { get; set; }
    }

    /// <summary>
    /// ترحيل إذن التنفيذ: مستند مخزون داخلي (FEFO) + قيد تكلفة البضاعة المباعة،
    /// ثم تحديث الكميات المنجزة وحالة الحجز — كل ذلك في معاملة واحدة.
    /// </summary>
    public async Task<DeliveryNote> PostAsync(Guid id, Guid userId, bool allowNegative,
        bool autoInvoice = false, CancellationToken ct = default)
    {
        await using var connection = await _factory.OpenAsync(ct);
        await using var tx = await connection.BeginTransactionAsync(ct);

        var head = await connection.QuerySingleOrDefaultAsync<(Guid Id, Guid BranchId,
            DateTime DocDate, string Status, Guid BookingId, Guid WarehouseId)>(new CommandDefinition("""
            SELECT id, branch_id, doc_date, status, booking_id, warehouse_id
            FROM delivery_notes WHERE id = @id FOR UPDATE
            """, new { id }, tx, cancellationToken: ct));
        if (head.Id == Guid.Empty) throw new NotFoundAppException();
        if (head.Status != DeliveryStatuses.Draft)
            throw new ValidationAppException("validation.document_not_draft");

        await connection.ExecuteAsync(new CommandDefinition(
            "SELECT fn_assert_period_open(@docDate::date, @branchId, 'ACCOUNTING')",
            new { docDate = head.DocDate, branchId = head.BranchId }, tx, cancellationToken: ct));

        var lines = (await connection.QueryAsync<PostLine>(new CommandDefinition("""
            SELECT l.id AS Id, l.line_no AS LineNo, l.booking_line_id AS BookingLineId,
                   l.item_id AS ItemId, l.unit_id AS UnitId, l.quantity AS Quantity,
                   b.quantity - b.delivered_qty - b.cancelled_qty AS Remaining
            FROM delivery_note_lines l
            JOIN booking_lines b ON b.id = l.booking_line_id
            WHERE l.delivery_note_id = @id ORDER BY l.line_no
            """, new { id }, tx, cancellationToken: ct))).ToList();
        if (lines.Count == 0)
            throw new ValidationAppException("validation.document_has_no_lines");
        if (lines.Any(l => l.Quantity > l.Remaining))
            throw new ValidationAppException("validation.delivery_exceeds_booking");

        var stockDocId = Guid.CreateVersion7();
        await connection.ExecuteAsync(new CommandDefinition("""
            INSERT INTO stock_documents (id, doc_type, doc_date, branch_id, warehouse_id, status,
                notes, created_by)
            VALUES (@stockDocId, @docType, @docDate, @branchId, @warehouseId, 'DRAFT', @notes, @userId)
            """, new
        {
            stockDocId, docType = StockDocumentTypes.Issue, docDate = head.DocDate,
            branchId = head.BranchId, warehouseId = head.WarehouseId,
            notes = $"إذن تنفيذ {id:N}", userId
        }, tx, cancellationToken: ct));

        foreach (var line in lines)
        {
            await connection.ExecuteAsync(new CommandDefinition("""
                INSERT INTO stock_document_lines (document_id, line_no, direction, item_id, unit_id,
                    conversion_factor, quantity, base_quantity, unit_cost)
                VALUES (@stockDocId, @LineNo, 'OUT', @ItemId, @UnitId, 1, @Quantity, @Quantity, 0)
                """, new { stockDocId, line.LineNo, line.ItemId, line.UnitId, line.Quantity },
                tx, cancellationToken: ct));
        }

        // تحرير الحجز قبل الصرف كي لا يتعارض قيد reserved_qty <= on_hand
        await connection.ExecuteAsync(new CommandDefinition("""
            UPDATE stock_balances sb SET reserved_qty = GREATEST(sb.reserved_qty - l.quantity, 0),
                updated_at = now()
            FROM delivery_note_lines l
            WHERE l.delivery_note_id = @id
              AND sb.item_id = l.item_id AND sb.warehouse_id = @warehouseId
            """, new { id, warehouseId = head.WarehouseId }, tx, cancellationToken: ct));

        await StockPostingRepository.PostCoreAsync(connection, tx, stockDocId, userId,
            allowNegative, ct);

        var costs = (await connection.QueryAsync<(int LineNo, decimal TotalCost)>(new CommandDefinition(
            "SELECT line_no, total_cost FROM stock_document_lines WHERE document_id = @stockDocId",
            new { stockDocId }, tx, cancellationToken: ct))).ToDictionary(x => x.LineNo, x => x.TotalCost);

        decimal totalCost = 0;
        foreach (var line in lines)
        {
            var cost = Math.Round(costs.TryGetValue(line.LineNo, out var value) ? value : 0m, 2,
                MidpointRounding.AwayFromZero);
            totalCost += cost;
            await connection.ExecuteAsync(new CommandDefinition("""
                UPDATE delivery_note_lines SET unit_cost = @unitCost, cost_total = @cost
                WHERE id = @id
                """, new { unitCost = line.Quantity == 0 ? 0 : Math.Round(cost / line.Quantity, 6),
                    cost, id = line.Id }, tx, cancellationToken: ct));

            await connection.ExecuteAsync(new CommandDefinition("""
                UPDATE booking_lines SET delivered_qty = delivered_qty + @quantity,
                    reserved_qty = GREATEST(reserved_qty - @quantity, 0)
                WHERE id = @bookingLineId
                """, new { quantity = line.Quantity, bookingLineId = line.BookingLineId }, tx,
                cancellationToken: ct));
        }

        var docNo = await connection.ExecuteScalarAsync<string>(new CommandDefinition("""
            SELECT next_document_number('DELIVERY'::varchar, @branchId::uuid, @docDate::date,
                                        'DN'::varchar, 6::smallint)
            """, new { branchId = head.BranchId, docDate = head.DocDate }, tx,
            cancellationToken: ct));

        Guid? entryId = totalCost > 0
            ? await CreateCogsEntryAsync(connection, tx, head.BranchId, head.DocDate, docNo,
                totalCost, id, userId, ct)
            : null;

        var remaining = await connection.ExecuteScalarAsync<decimal>(new CommandDefinition("""
            SELECT COALESCE(SUM(quantity - delivered_qty - cancelled_qty), 0)
            FROM booking_lines WHERE booking_id = @bookingId
            """, new { bookingId = head.BookingId }, tx, cancellationToken: ct));

        await connection.ExecuteAsync(new CommandDefinition(
            "UPDATE bookings SET status = @status, updated_by = @userId WHERE id = @bookingId",
            new { status = remaining <= 0 ? BookingStatuses.Completed : BookingStatuses.PartiallyDelivered,
                userId, bookingId = head.BookingId }, tx, cancellationToken: ct));

        await connection.ExecuteAsync(new CommandDefinition("""
            UPDATE delivery_notes SET status = 'posted', doc_no = @docNo, posted_at = now(),
                posted_by = @userId, journal_entry_id = @entryId, stock_document_id = @stockDocId,
                updated_by = @userId
            WHERE id = @id
            """, new { docNo, userId, entryId, stockDocId, id }, tx, cancellationToken: ct));

        if (autoInvoice)
            await SalesInvoiceRepository.CreateFromDeliveryAsync(connection, tx, id, userId, ct);

        await tx.CommitAsync(ct);
        return (await GetAsync(id, ct))!;
    }

    /// <summary>قيد التكلفة عند التنفيذ (N4): Dr تكلفة المبيعات / Cr المخزون.</summary>
    private static async Task<Guid> CreateCogsEntryAsync(NpgsqlConnection connection,
        NpgsqlTransaction tx, Guid branchId, DateTime docDate, string docNo, decimal amount,
        Guid deliveryId, Guid userId, CancellationToken ct)
    {
        var defaults = (await connection.QueryAsync<(string Key, Guid? AccountId)>(
            new CommandDefinition("SELECT setting_key, account_id FROM posting_defaults",
                transaction: tx, cancellationToken: ct)))
            .Where(r => r.AccountId.HasValue)
            .ToDictionary(r => r.Key, r => r.AccountId!.Value, StringComparer.OrdinalIgnoreCase);

        Guid Need(string key) => defaults.TryGetValue(key, out var account) ? account
            : throw new ValidationAppException("validation.posting_account_missing",
                new Dictionary<string, string> { ["key"] = key, ["module"] = "sales" });

        var journalId = await connection.ExecuteScalarAsync<Guid?>(new CommandDefinition(
            "SELECT id FROM journals WHERE journal_type = 'stock' AND is_active " +
            "ORDER BY sort_order LIMIT 1", transaction: tx, cancellationToken: ct))
            ?? throw new ValidationAppException("validation.journal_not_found");

        var entryId = Guid.CreateVersion7();
        await connection.ExecuteAsync(new CommandDefinition("""
            INSERT INTO journal_entries (id, journal_id, branch_id, entry_date, reference,
                description, status, source_type, source_id, total_debit, total_credit, created_by)
            VALUES (@entryId, @journalId, @branchId, @docDate, @docNo, @description, 'draft',
                    'delivery', @deliveryId, @amount, @amount, @userId)
            """, new { entryId, journalId, branchId, docDate, docNo, deliveryId, amount, userId,
                description = $"تكلفة تنفيذ {docNo}" }, tx, cancellationToken: ct));

        var gl = new[]
        {
            (Account: Need(PostingKeys.StockCogs), Debit: amount, Credit: 0m),
            (Account: Need(PostingKeys.StockInventory), Debit: 0m, Credit: amount)
        };
        var lineNo = 0;
        foreach (var line in gl)
        {
            lineNo++;
            await connection.ExecuteAsync(new CommandDefinition("""
                INSERT INTO journal_entry_lines (id, entry_id, line_no, account_id, description,
                    debit, credit, branch_id, entry_date)
                VALUES (@lineId, @entryId, @lineNo, @accountId, @description, @debit, @credit,
                        @branchId, @docDate)
                """, new
            {
                lineId = Guid.CreateVersion7(), entryId, lineNo, accountId = line.Account,
                description = $"تكلفة تنفيذ {docNo}", debit = line.Debit, credit = line.Credit,
                branchId, docDate
            }, tx, cancellationToken: ct));
        }

        await connection.ExecuteAsync(new CommandDefinition(
            "SELECT fn_gl_post_entry(@entryId, @userId)", new { entryId, userId }, tx,
            cancellationToken: ct));
        return entryId;
    }
}
