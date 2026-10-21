using Dapper;
using Npgsql;
using Ubp.Core.Models;
using Ubp.Core.Results;

namespace Ubp.Infrastructure.Repositories;

public sealed partial class StockPostingRepository
{
    public async Task<StockDocument> ReverseAsync(Guid documentId, Guid userId, bool allowNegative,
        CancellationToken ct = default)
    {
        await using var connection = await _factory.OpenAsync(ct);
        await using var tx = await connection.BeginTransactionAsync(ct);

        var header = await LockHeaderAsync(connection, tx, documentId, ct);
        if (header.Status is StockStatuses.Draft or StockStatuses.Cancelled)
            throw new ValidationAppException("validation.document_not_posted");
        if (header.DocType == StockDocumentTypes.Transfer)
            throw new ValidationAppException("validation.transfer_reverse_not_supported");
        if (header.DocType == StockDocumentTypes.Reversal)
            throw new ValidationAppException("validation.reversal_not_allowed");

        var alreadyReversed = await connection.ExecuteScalarAsync<bool>(new CommandDefinition(
            "SELECT EXISTS (SELECT 1 FROM stock_documents WHERE reversal_of_id = @documentId)",
            new { documentId }, tx, cancellationToken: ct));
        if (alreadyReversed)
            throw new ValidationAppException("validation.document_already_reversed");

        var reversalDate = DateTime.UtcNow.Date;
        if (await PeriodClosedAsync(connection, tx, reversalDate, header.BranchId, ct))
            throw new ValidationAppException("validation.period_closed");

        var newId = await connection.ExecuteScalarAsync<Guid>(new CommandDefinition("""
            INSERT INTO stock_documents
                (doc_type, doc_date, branch_id, warehouse_id, notes, reversal_of_id, created_by)
            VALUES
                ('REVERSAL', @reversalDate, @BranchId, @WarehouseId,
                 @notes, @documentId, @userId)
            RETURNING id
            """, new
        {
            reversalDate, header.BranchId, header.WarehouseId,
            notes = $"عكس المستند {header.DocType}", documentId, userId
        }, tx, cancellationToken: ct));

        var lines = (await connection.QueryAsync<PostLine>(new CommandDefinition("""
                SELECT id AS Id, line_no AS LineNo, direction AS Direction, item_id AS ItemId,
                       unit_id AS UnitId, quantity AS Quantity, base_quantity AS BaseQuantity,
                       unit_cost AS UnitCost, conversion_factor AS ConversionFactor
                FROM stock_document_lines WHERE document_id = @documentId ORDER BY line_no
                """, new { documentId }, tx, cancellationToken: ct))).ToList();

        const string insertLine = """
            INSERT INTO stock_document_lines
                (document_id, line_no, direction, item_id, unit_id, conversion_factor,
                 quantity, base_quantity, unit_cost, notes)
            VALUES
                (@newId, @LineNo, @Direction, @ItemId, @UnitId, @ConversionFactor,
                 @Quantity, @BaseQuantity, @UnitCost, @Notes)
            """;
        foreach (var line in lines)
        {
            await connection.ExecuteAsync(new CommandDefinition(insertLine, new
            {
                newId, line.LineNo,
                Direction = line.Direction == "IN" ? "OUT" : "IN",
                line.ItemId, line.UnitId, line.ConversionFactor,
                line.Quantity, line.BaseQuantity, line.UnitCost,
                Notes = "عكس الحركة الأصلية"
            }, tx, cancellationToken: ct));
        }

        await PostCoreAsync(connection, tx, newId, userId, allowNegative, ct);
        await tx.CommitAsync(ct);
        return (await GetDocumentAsync(connection, newId, ct))!;
    }
}
