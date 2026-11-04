using Dapper;
using Npgsql;

namespace Ubp.Infrastructure.Repositories;

public sealed partial class OperatingExpenseRepository
{
    /// <summary>استلام مخزني لكل مخزن (يُربط بكل سطر) وتُرحَّل الحركة عبر محرك المخزون.</summary>
    private static async Task<Guid?> PostStockLinesAsync(NpgsqlConnection connection,
        NpgsqlTransaction tx, PostHeader head, IReadOnlyList<PostLine> lines, string docNo,
        Guid userId, CancellationToken ct)
    {
        var stockLines = lines
            .Where(l => l.ItemId is not null && l.WarehouseId is not null)
            .ToList();
        if (stockLines.Count == 0) return null;

        Guid? firstDocumentId = null;
        foreach (var group in stockLines.GroupBy(l => l.WarehouseId!.Value))
        {
            var documentId = Guid.CreateVersion7();
            await connection.ExecuteAsync(new CommandDefinition("""
                INSERT INTO stock_documents
                    (id, doc_type, doc_date, branch_id, warehouse_id, status, notes, created_by)
                VALUES (@id, 'RECEIPT', @date, @branchId, @warehouseId, 'DRAFT', @notes, @userId)
                """, new
            {
                id = documentId, date = head.DocDate.Date, branchId = head.BranchId,
                warehouseId = group.Key, notes = $"مصروفات تشغيلية {docNo}", userId
            }, tx, cancellationToken: ct));

            var lineNo = 0;
            foreach (var line in group)
            {
                lineNo++;
                await connection.ExecuteAsync(new CommandDefinition("""
                    INSERT INTO stock_document_lines
                        (document_id, line_no, direction, item_id, unit_id, conversion_factor,
                         quantity, base_quantity, unit_cost)
                    VALUES (@documentId, @lineNo, 'IN', @itemId, @unitId, 1, @quantity, @quantity,
                            @unitCost)
                    """, new
                {
                    documentId, lineNo, itemId = line.ItemId, unitId = line.UnitId,
                    quantity = line.Quantity, unitCost = line.UnitPrice
                }, tx, cancellationToken: ct));

                await connection.ExecuteAsync(new CommandDefinition("""
                    UPDATE operating_expense_lines SET stock_document_id = @documentId
                    WHERE id = @lineId
                    """, new { documentId, lineId = line.Id }, tx, cancellationToken: ct));
            }

            await StockPostingRepository.PostCoreAsync(connection, tx, documentId, userId,
                allowNegative: false, ct);
            firstDocumentId ??= documentId;
        }

        return firstDocumentId;
    }
}
