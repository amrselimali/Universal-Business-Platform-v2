using Dapper;
using Npgsql;
using Ubp.Core.Models;

namespace Ubp.Infrastructure.Repositories;

public sealed partial class CommercialDocumentRepository
{
    /// <summary>مستند مخزون داخلي يحمل نفس بنود المستند التجاري ثم يُرحّل بمحرك المخزون.</summary>
    private static async Task<Guid> CreateStockDocumentAsync(NpgsqlConnection connection,
        NpgsqlTransaction tx, PostHead head, IReadOnlyList<PostLine> lines, bool isReturn,
        string docNo, Guid userId, bool allowNegative, CancellationToken ct)
    {
        var stockDocId = Guid.CreateVersion7();
        await connection.ExecuteAsync(new CommandDefinition("""
            INSERT INTO stock_documents (id, doc_type, doc_date, branch_id, warehouse_id, status,
                notes, created_by)
            VALUES (@stockDocId, @stockType, @docDate, @branchId, @warehouseId, 'DRAFT', @notes, @userId)
            """, new
        {
            stockDocId, stockType = isReturn ? StockDocumentTypes.Issue : StockDocumentTypes.Receipt,
            docDate = head.DocDate, branchId = head.BranchId, warehouseId = head.WarehouseId,
            notes = $"مستند تجاري {docNo}", userId
        }, tx, cancellationToken: ct));

        foreach (var line in lines)
        {
            await connection.ExecuteAsync(new CommandDefinition("""
                INSERT INTO stock_document_lines (document_id, line_no, direction, item_id, unit_id,
                    conversion_factor, quantity, base_quantity, unit_cost, lot_number, expiry_date)
                VALUES (@stockDocId, @LineNo, @direction, @ItemId, @UnitId, @ConversionFactor,
                        @Quantity, @baseQuantity, @unitCost, @LotNumber, @ExpiryDate)
                """, new
            {
                stockDocId, line.LineNo, direction = isReturn ? "OUT" : "IN", line.ItemId,
                line.UnitId, line.ConversionFactor, line.Quantity,
                baseQuantity = Math.Round(line.Quantity * line.ConversionFactor, 4),
                unitCost = isReturn ? 0 : Math.Round(line.NetAmount / line.Quantity, 6),
                line.LotNumber, line.ExpiryDate
            }, tx, cancellationToken: ct));
        }

        await StockPostingRepository.PostCoreAsync(connection, tx, stockDocId, userId,
            allowNegative, ct);
        return stockDocId;
    }
}
