using Dapper;
using Npgsql;
using Ubp.Core.Models;
using Ubp.Core.Results;

namespace Ubp.Infrastructure.Repositories;

public sealed partial class BookingExecutionRepository
{
    private sealed class ExecutionLine
    {
        public Guid BookingId { get; set; }
        public Guid BranchId { get; set; }
        public string BookingStatus { get; set; } = "";
        public Guid ItemId { get; set; }
        public string ItemType { get; set; } = "GOODS";
        public Guid UnitId { get; set; }
        public Guid WarehouseId { get; set; }
        public decimal Quantity { get; set; }
        public decimal DeliveredQty { get; set; }
        public decimal CancelledQty { get; set; }
        public decimal ReservedQty { get; set; }
        public Guid? MeteredAssetId { get; set; }
    }

    public async Task<BookingExecutionResult> ExecuteAsync(BookingExecutionInput input,
        Guid userId, bool allowNegative, CancellationToken ct = default)
    {
        if (input.Quantity <= 0)
            throw new ValidationAppException("validation.quantity_invalid");
        if (input.MeterUnits < 0)
            throw new ValidationAppException("validation.meter_value_invalid");

        await using var connection = await _factory.OpenAsync(ct);
        await using var tx = await connection.BeginTransactionAsync(ct);

        var line = await connection.QuerySingleOrDefaultAsync<ExecutionLine>(new CommandDefinition("""
            SELECT l.booking_id AS BookingId, b.branch_id AS BranchId, b.status AS BookingStatus,
                   l.item_id AS ItemId, i.item_type AS ItemType, l.unit_id AS UnitId,
                   l.warehouse_id AS WarehouseId, l.quantity AS Quantity,
                   l.delivered_qty AS DeliveredQty, l.cancelled_qty AS CancelledQty,
                   l.reserved_qty AS ReservedQty, i.metered_asset_id AS MeteredAssetId
            FROM booking_lines l
            JOIN bookings b ON b.id = l.booking_id
            JOIN items i ON i.id = l.item_id
            WHERE l.id = @lineId
            FOR UPDATE OF l, b
            """, new { lineId = input.BookingLineId }, tx, cancellationToken: ct))
            ?? throw new NotFoundAppException();

        if (line.BookingStatus is not (BookingStatuses.Confirmed or BookingStatuses.PartiallyDelivered))
            throw new ValidationAppException("validation.booking_not_confirmed");

        var remaining = line.Quantity - line.DeliveredQty - line.CancelledQty;
        if (input.Quantity > remaining)
            throw new ValidationAppException("validation.booking_quantity_exceeds_remaining");
        if (input.MeterUnits > 0 && line.MeteredAssetId is null)
            throw new ValidationAppException("validation.item_has_no_metered_asset");

        var executionId = Guid.CreateVersion7();
        var executionLineId = Guid.CreateVersion7();
        var docNo = await connection.ExecuteScalarAsync<string>(new CommandDefinition("""
            SELECT next_document_number('EXECUTION'::varchar, @branchId::uuid, @date::date,
                                        'EX'::varchar, 6::smallint)
            """, new { branchId = line.BranchId, date = DateTime.UtcNow.Date }, tx,
            cancellationToken: ct));

        Guid? stockDocumentId = null;
        Guid? stockDocumentLineId = null;
        var cost = 0m;
        if (line.ItemType == "GOODS")
        {
            // حرر حجز المخزون قبل الصرف حتى لا يتعارض reserved_qty <= on_hand.
            await connection.ExecuteAsync(new CommandDefinition("""
                UPDATE stock_balances
                SET reserved_qty = GREATEST(reserved_qty - @quantity, 0), updated_at = now()
                WHERE item_id = @itemId AND warehouse_id = @warehouseId
                """, new { quantity = input.Quantity, itemId = line.ItemId,
                    warehouseId = line.WarehouseId }, tx, cancellationToken: ct));

            stockDocumentId = Guid.CreateVersion7();
            await connection.ExecuteAsync(new CommandDefinition("""
                INSERT INTO stock_documents
                    (id, doc_type, doc_date, branch_id, warehouse_id, status, notes, created_by)
                VALUES (@id, 'ISSUE', @date, @branchId, @warehouseId, 'DRAFT', @notes, @userId)
                """, new
            {
                id = stockDocumentId, date = DateTime.UtcNow.Date, branchId = line.BranchId,
                warehouseId = line.WarehouseId, notes = $"تنفيذ حجز {docNo}", userId
            }, tx, cancellationToken: ct));

            stockDocumentLineId = Guid.CreateVersion7();
            await connection.ExecuteAsync(new CommandDefinition("""
                INSERT INTO stock_document_lines
                    (id, document_id, line_no, direction, item_id, unit_id, conversion_factor,
                     quantity, base_quantity, unit_cost)
                VALUES (@id, @documentId, 1, 'OUT', @itemId, @unitId, 1, @quantity, @quantity, 0)
                """, new
            {
                id = stockDocumentLineId, documentId = stockDocumentId, itemId = line.ItemId,
                unitId = line.UnitId, quantity = input.Quantity
            }, tx, cancellationToken: ct));

            await StockPostingRepository.PostCoreAsync(connection, tx, stockDocumentId.Value,
                userId, allowNegative, ct);
            cost = await connection.ExecuteScalarAsync<decimal>(new CommandDefinition(
                "SELECT COALESCE(total_cost, 0) FROM stock_document_lines WHERE id = @id",
                new { id = stockDocumentLineId }, tx, cancellationToken: ct));
        }

        var journalEntryId = cost > 0
            ? await PostCogsAsync(connection, tx, line.BranchId, docNo, cost, executionId, userId, ct)
            : (Guid?)null;
        await connection.ExecuteAsync(new CommandDefinition("""
            INSERT INTO booking_executions
                (id, branch_id, doc_no, execution_date, booking_id, status, stock_document_id,
                 journal_entry_id, notes, created_by)
            VALUES (@id, @branchId, @docNo, @date, @bookingId, 'posted', @stockDocumentId,
                    @journalEntryId, @notes, @userId)
            """, new
        {
            id = executionId, line.BranchId, docNo, date = DateTime.UtcNow.Date,
            bookingId = line.BookingId, stockDocumentId, journalEntryId,
            notes = "تنفيذ من شاشة الحجوزات", userId
        }, tx, cancellationToken: ct));

        await connection.ExecuteAsync(new CommandDefinition("""
            INSERT INTO booking_execution_lines
                (id, execution_id, booking_line_id, item_id, unit_id, quantity, unit_cost,
                 cost_total, stock_document_line_id, metered_asset_id, meter_units)
            VALUES (@id, @executionId, @bookingLineId, @itemId, @unitId, @quantity, @unitCost,
                    @cost, @stockDocumentLineId, @meteredAssetId, @meterUnits)
            """, new
        {
            id = executionLineId, executionId, bookingLineId = input.BookingLineId,
            itemId = line.ItemId, unitId = line.UnitId, quantity = input.Quantity,
            unitCost = input.Quantity == 0 ? 0 : Math.Round(cost / input.Quantity, 6),
            cost, stockDocumentLineId, meteredAssetId = line.MeteredAssetId,
            meterUnits = input.MeterUnits
        }, tx, cancellationToken: ct));

        await connection.ExecuteAsync(new CommandDefinition("""
            UPDATE booking_lines
            SET delivered_qty = delivered_qty + @quantity,
                reserved_qty = GREATEST(reserved_qty - @quantity, 0)
            WHERE id = @id
            """, new { quantity = input.Quantity, id = input.BookingLineId }, tx,
            cancellationToken: ct));

        await connection.ExecuteAsync(new CommandDefinition("""
            UPDATE bookings b
            SET status = CASE WHEN NOT EXISTS (
                    SELECT 1 FROM booking_lines l
                    WHERE l.booking_id = b.id
                      AND (l.quantity - l.delivered_qty - l.cancelled_qty) > 0)
                THEN 'completed' ELSE 'partially_delivered' END,
                updated_by = @userId
            WHERE b.id = @bookingId
            """, new { bookingId = line.BookingId, userId }, tx, cancellationToken: ct));

        var meterValueAfter = 0m;
        if (input.MeterUnits > 0 && line.MeteredAssetId is { } assetId)
        {
            var asset = await connection.QuerySingleOrDefaultAsync<(decimal Current, bool IsMetered,
                string Status, bool Resets)>(new CommandDefinition("""
                SELECT current_meter_value AS Current, is_metered AS IsMetered, status AS Status,
                       meter_resets_on_maintenance AS Resets
                FROM fixed_assets WHERE id = @id FOR UPDATE
                """, new { id = assetId }, tx, cancellationToken: ct));
            if (!asset.IsMetered || asset.Status == AssetStatuses.Disposed)
                throw new ValidationAppException("validation.asset_not_metered");

            meterValueAfter = asset.Current + input.MeterUnits;
            await connection.ExecuteAsync(new CommandDefinition("""
                INSERT INTO asset_meter_readings
                    (asset_id, value, delta, source, note, source_line_id, created_by)
                VALUES (@assetId, @value, @delta, 'EXECUTION', @note, @sourceLineId, @userId)
                """, new
            {
                assetId, value = meterValueAfter, delta = input.MeterUnits,
                note = $"تنفيذ حجز {docNo}", sourceLineId = executionLineId, userId
            }, tx, cancellationToken: ct));
            await connection.ExecuteAsync(new CommandDefinition("""
                UPDATE fixed_assets
                SET current_meter_value = @value, updated_by = @userId, updated_at = now(),
                    version = version + 1
                WHERE id = @id
                """, new { value = meterValueAfter, userId, id = assetId }, tx,
                cancellationToken: ct));
        }

        await tx.CommitAsync(ct);
        return new BookingExecutionResult
        {
            ExecutionId = executionId, BookingId = line.BookingId, DocNo = docNo,
            Quantity = input.Quantity, MeterUnits = input.MeterUnits,
            MeterValueAfter = meterValueAfter, CostTotal = cost
        };
    }

    private static async Task<Guid> PostCogsAsync(NpgsqlConnection connection,
        NpgsqlTransaction tx, Guid branchId, string docNo, decimal amount, Guid executionId,
        Guid userId, CancellationToken ct)
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
            INSERT INTO journal_entries
                (id, journal_id, branch_id, entry_date, reference, description, status, source_type,
                 source_id, total_debit, total_credit, created_by)
            VALUES (@entryId, @journalId, @branchId, @date, @docNo, @description, 'draft',
                    'execution', @executionId, @amount, @amount, @userId)
            """, new
        {
            entryId, journalId, branchId, date = DateTime.UtcNow.Date, docNo,
            description = $"تكلفة تنفيذ {docNo}", executionId, amount, userId
        }, tx, cancellationToken: ct));

        var cogs = Need(PostingKeys.StockCogs);
        var inventory = Need(PostingKeys.StockInventory);
        await connection.ExecuteAsync(new CommandDefinition("""
            INSERT INTO journal_entry_lines
                (id, entry_id, line_no, account_id, description, debit, credit, branch_id,
                 entry_date)
            VALUES (@id, @entryId, 1, @account, @description, @amount, 0, @branchId, @date),
                   (@id2, @entryId, 2, @account2, @description, 0, @amount, @branchId, @date)
            """, new
        {
            id = Guid.CreateVersion7(), id2 = Guid.CreateVersion7(), entryId,
            account = cogs, account2 = inventory, description = $"تكلفة تنفيذ {docNo}",
            amount, branchId, date = DateTime.UtcNow.Date
        }, tx, cancellationToken: ct));

        await connection.ExecuteAsync(new CommandDefinition(
            "SELECT fn_gl_post_entry(@entryId, @userId)", new { entryId, userId }, tx,
            cancellationToken: ct));
        return entryId;
    }
}
