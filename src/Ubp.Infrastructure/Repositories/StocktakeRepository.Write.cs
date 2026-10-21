using Dapper;
using Ubp.Core.Models;

namespace Ubp.Infrastructure.Repositories;

public sealed partial class StocktakeRepository
{
    public async Task<Stocktake> CreateAsync(StocktakeInput input, string code, Guid userId,
        CancellationToken ct = default)
    {
        const string sql = """
            INSERT INTO stocktakes (code, branch_id, warehouse_id, notes, created_by)
            VALUES (@code, @BranchId, @WarehouseId, @Notes, @userId)
            RETURNING id
            """;
        const string linesSql = """
            INSERT INTO stocktake_lines (stocktake_id, item_id, book_qty, counted_qty, unit_cost)
            SELECT @id, i.id, COALESCE(b.on_hand, 0), COALESCE(b.on_hand, 0),
                   COALESCE(b.average_cost, 0)
            FROM items i
            LEFT JOIN stock_balances b
                   ON b.item_id = i.id AND b.warehouse_id = @warehouseId
            WHERE i.is_active AND i.item_type = 'GOODS'
            ORDER BY i.code
            """;

        await using var connection = await _factory.OpenAsync(ct);
        await using var tx = await connection.BeginTransactionAsync(ct);
        var id = await connection.ExecuteScalarAsync<Guid>(new CommandDefinition(sql, new
        {
            code, input.BranchId, input.WarehouseId, input.Notes, userId
        }, tx, cancellationToken: ct));
        await connection.ExecuteAsync(new CommandDefinition(linesSql,
            new { id, input.WarehouseId }, tx, cancellationToken: ct));
        await tx.CommitAsync(ct);
        return (await GetAsync(id, ct))!;
    }

    public async Task UpdateLinesAsync(Guid id, IReadOnlyList<StocktakeLineInput> lines,
        CancellationToken ct = default)
    {
        const string sql = """
            UPDATE stocktake_lines
               SET counted_qty = @CountedQty, unit_cost = @UnitCost, reason = @Reason
             WHERE stocktake_id = @id AND item_id = @ItemId
            """;
        await using var connection = await _factory.OpenAsync(ct);
        await using var tx = await connection.BeginTransactionAsync(ct);
        foreach (var line in lines)
        {
            await connection.ExecuteAsync(new CommandDefinition(sql, new
            {
                id, line.ItemId, line.CountedQty, line.UnitCost, line.Reason
            }, tx, cancellationToken: ct));
        }
        await tx.CommitAsync(ct);
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        await using var connection = await _factory.OpenAsync(ct);
        return await connection.ExecuteAsync(new CommandDefinition(
            "DELETE FROM stocktakes WHERE id = @id AND status = 'DRAFT'",
            new { id }, cancellationToken: ct)) > 0;
    }

    public async Task MarkPostedAsync(Guid id, Guid documentId, Guid userId, CancellationToken ct = default)
    {
        await using var connection = await _factory.OpenAsync(ct);
        await connection.ExecuteAsync(new CommandDefinition("""
            UPDATE stocktakes SET status = 'POSTED', posted_at = now(), posted_by = @userId
            WHERE id = @id AND status = 'DRAFT'
            """, new { id, userId }, cancellationToken: ct));
    }
}
