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
        public Guid CustomerId { get; set; }
        public string BookingStatus { get; set; } = "";
        public Guid ItemId { get; set; }
        public Guid? CategoryId { get; set; }
        public string ItemType { get; set; } = "GOODS";
        public string ExecutionMode { get; set; } = ItemExecutionModes.Reserved;
        public Guid UnitId { get; set; }
        public Guid WarehouseId { get; set; }
        public decimal Quantity { get; set; }
        public decimal DeliveredQty { get; set; }
        public decimal CancelledQty { get; set; }
        public decimal ReservedQty { get; set; }
        public decimal NetAmount { get; set; }
        public decimal TaxAmount { get; set; }
        public decimal TotalAmount { get; set; }
        public bool ItemIsMetered { get; set; }
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
            SELECT l.booking_id AS BookingId, b.branch_id AS BranchId, b.customer_id AS CustomerId,
                   b.status AS BookingStatus, l.item_id AS ItemId, i.category_id AS CategoryId,
                   i.item_type AS ItemType, l.unit_id AS UnitId,
                   l.warehouse_id AS WarehouseId, l.quantity AS Quantity,
                   l.delivered_qty AS DeliveredQty, l.cancelled_qty AS CancelledQty,
                   l.reserved_qty AS ReservedQty, l.net_amount AS NetAmount,
                   l.tax_amount AS TaxAmount, l.total_amount AS TotalAmount,
                   i.is_metered AS ItemIsMetered, i.execution_mode AS ExecutionMode
            FROM booking_lines l
            JOIN bookings b ON b.id = l.booking_id
            JOIN items i ON i.id = l.item_id
            WHERE l.id = @lineId
            FOR UPDATE OF l, b
            """, new { lineId = input.BookingLineId }, tx, cancellationToken: ct))
            ?? throw new NotFoundAppException();

        var direct = string.Equals(line.ExecutionMode, ItemExecutionModes.Direct,
            StringComparison.OrdinalIgnoreCase);
        if (!direct &&
            line.BookingStatus is not (BookingStatuses.Confirmed or BookingStatuses.PartiallyDelivered))
            throw new ValidationAppException("validation.booking_not_confirmed");

        var remaining = line.Quantity - line.DeliveredQty - line.CancelledQty;
        if (input.Quantity > remaining)
            throw new ValidationAppException("validation.booking_quantity_exceeds_remaining");
        if (line.ItemIsMetered && !direct)
        {
            // الأصناف المرتبطة بعدّاد: الأصل والوحدات إجباريان (حصرًا على المرتبط بعدّاد).
            if (input.MeteredAssetId is null)
                throw new ValidationAppException("validation.asset_meter_required");
            if (input.MeterUnits <= 0)
                throw new ValidationAppException("validation.meter_units_required");
        }
        else if (!line.ItemIsMetered && (input.MeteredAssetId is not null || input.MeterUnits > 0))
        {
            throw new ValidationAppException("validation.item_not_metered");
        }

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

        // تكلفة الأصناف المخزنية: من حساب تكلفة المخزن (تصنيف الصنف) إلى حساب المخزن.
        var journalEntryId = cost > 0
            ? await PostCogsAsync(connection, tx, line.CategoryId, line.BranchId, docNo, cost,
                executionId, userId, ct)
            : (Guid?)null;

        // إيراد التنفيذ: للخدمة دائمًا، وللصنف المباشر أيًا كان نوعه (ذمم مباشرة).
        if (line.ItemType == "SERVICE" || direct)
            journalEntryId = await PostServiceRevenueAsync(connection, tx, line, input.Quantity,
                docNo, executionLineId, executionId, userId, ct) ?? journalEntryId;
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
            cost, stockDocumentLineId, meteredAssetId = input.MeteredAssetId,
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
        if (input.MeteredAssetId is { } assetId)
        {
            var asset = await connection.QuerySingleOrDefaultAsync<(decimal Current, bool IsMetered,
                string Status, Guid? BranchId)>(new CommandDefinition("""
                SELECT current_meter_value AS Current, is_metered AS IsMetered, status AS Status,
                       branch_id AS BranchId
                FROM fixed_assets WHERE id = @id FOR UPDATE
                """, new { id = assetId }, tx, cancellationToken: ct));
            if (!asset.IsMetered || asset.Status != AssetStatuses.Active ||
                (asset.BranchId is not null && asset.BranchId != line.BranchId))
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
}
