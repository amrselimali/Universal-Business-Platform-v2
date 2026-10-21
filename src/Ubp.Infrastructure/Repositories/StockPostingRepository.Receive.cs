using Dapper;
using Npgsql;
using Ubp.Core.Models;
using Ubp.Core.Results;

namespace Ubp.Infrastructure.Repositories;

public sealed partial class StockPostingRepository
{
    public async Task<StockDocument> ReceiveTransferAsync(TransferReceiptInput input, Guid userId,
        bool allowNegative, CancellationToken ct = default)
    {
        if (input.Lines.Count == 0)
            throw new ValidationAppException("validation.receipt_has_no_lines");
        if (input.Lines.Select(l => l.DocumentLineId).Distinct().Count() != input.Lines.Count)
            throw new ValidationAppException("validation.receipt_duplicate_lines");

        await using var connection = await _factory.OpenAsync(ct);
        await using var tx = await connection.BeginTransactionAsync(ct);

        var header = await LockHeaderAsync(connection, tx, input.DocumentId, ct);
        if (header.DocType != StockDocumentTypes.Transfer)
            throw new ValidationAppException("validation.not_a_transfer");
        if (header.Status is not (StockStatuses.Posted or StockStatuses.PartiallyReceived))
            throw new ValidationAppException("validation.transfer_not_receivable");
        if (!header.ToWarehouseId.HasValue || !header.ToBranchId.HasValue)
            throw new ValidationAppException("validation.transfer_destination_missing");

        var receiptDate = DateTime.UtcNow.Date;
        if (await PeriodClosedAsync(connection, tx, receiptDate, header.ToBranchId.Value, ct))
            throw new ValidationAppException("validation.period_closed");

        var periodId = await connection.ExecuteScalarAsync<Guid?>(new CommandDefinition("""
            SELECT id FROM fiscal_periods WHERE @receiptDate::date BETWEEN start_date AND end_date
            ORDER BY start_date DESC LIMIT 1
            """, new { receiptDate }, tx, cancellationToken: ct));

        var receiptId = await connection.ExecuteScalarAsync<Guid>(new CommandDefinition("""
            INSERT INTO stock_transfer_receipts (document_id, received_by, notes)
            VALUES (@documentId, @userId, @notes) RETURNING id
            """, new { documentId = input.DocumentId, userId, input.Notes }, tx, cancellationToken: ct));

        const string insertReceiptLine = """
            INSERT INTO stock_transfer_receipt_lines (receipt_id, document_line_id, quantity)
            VALUES (@receiptId, @lineId, @quantity)
            """;
        const string insertMovement = """
            INSERT INTO stock_movements
                (document_id, document_line_id, item_id, warehouse_id, branch_id, direction,
                 quantity, unit_cost, total_cost, movement_date, period_id, created_by)
            VALUES
                (@documentId, @lineId, @itemId, @warehouseId, @branchId, 'IN',
                 @quantity, @unitCost, @totalCost, @receiptDate, @periodId, @userId)
            """;

        foreach (var line in input.Lines)
        {
            if (line.Quantity <= 0)
                throw new ValidationAppException("validation.quantity_invalid");

            var docLine = await connection.QuerySingleOrDefaultAsync<ReceiveLine>(new CommandDefinition("""
                SELECT id AS Id, item_id AS ItemId, base_quantity AS BaseQuantity,
                       unit_cost AS UnitCost
                FROM stock_document_lines
                WHERE id = @lineId AND document_id = @documentId
                """, new { lineId = line.DocumentLineId, documentId = input.DocumentId }, tx, cancellationToken: ct))
                ?? throw new ValidationAppException("validation.document_line_not_found");

            var received = await connection.ExecuteScalarAsync<decimal>(new CommandDefinition(
                "SELECT COALESCE(sum(quantity),0) FROM stock_transfer_receipt_lines WHERE document_line_id = @lineId",
                new { lineId = line.DocumentLineId }, tx, cancellationToken: ct));

            if (received + line.Quantity > docLine.BaseQuantity)
                throw new ValidationAppException("validation.receipt_exceeds_remaining");

            decimal moveCost;
            try
            {
                moveCost = await connection.ExecuteScalarAsync<decimal>(new CommandDefinition("""
                    SELECT fn_stock_apply(@itemId, @warehouseId, 'IN', @quantity, @unitCost,
                                          @allowNegative, false)
                    """, new
                {
                    docLine.ItemId, warehouseId = header.ToWarehouseId.Value,
                    quantity = line.Quantity, unitCost = docLine.UnitCost, allowNegative
                }, tx, cancellationToken: ct));
            }
            catch (PostgresException ex) when (ex.MessageText.StartsWith("NEGATIVE_STOCK", StringComparison.Ordinal))
            {
                throw new ValidationAppException("validation.negative_stock");
            }

            await connection.ExecuteAsync(new CommandDefinition(insertReceiptLine, new
            {
                receiptId, lineId = line.DocumentLineId, quantity = line.Quantity
            }, tx, cancellationToken: ct));

            await connection.ExecuteAsync(new CommandDefinition(insertMovement, new
            {
                documentId = input.DocumentId, lineId = line.DocumentLineId, docLine.ItemId,
                warehouseId = header.ToWarehouseId.Value, branchId = header.ToBranchId.Value,
                quantity = line.Quantity, unitCost = docLine.UnitCost, totalCost = moveCost,
                receiptDate, periodId, userId
            }, tx, cancellationToken: ct));
        }

        var remaining = await connection.ExecuteScalarAsync<int>(new CommandDefinition("""
            SELECT count(*)::int FROM stock_document_lines l
            WHERE l.document_id = @documentId
              AND l.base_quantity > COALESCE((SELECT sum(rl.quantity) FROM stock_transfer_receipt_lines rl
                                              WHERE rl.document_line_id = l.id), 0)
            """, new { documentId = input.DocumentId }, tx, cancellationToken: ct));

        await connection.ExecuteAsync(new CommandDefinition(
            "UPDATE stock_documents SET status = @status WHERE id = @documentId",
            new
            {
                status = remaining > 0 ? StockStatuses.PartiallyReceived : StockStatuses.Received,
                documentId = input.DocumentId
            }, tx, cancellationToken: ct));

        await tx.CommitAsync(ct);
        return (await GetDocumentAsync(connection, input.DocumentId, ct))!;
    }

    private sealed class ReceiveLine
    {
        public Guid Id { get; set; }
        public Guid ItemId { get; set; }
        public decimal BaseQuantity { get; set; }
        public decimal UnitCost { get; set; }
    }
}
