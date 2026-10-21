using Dapper;
using Ubp.Core.Models;
using Ubp.Core.Paging;
using Ubp.Infrastructure.Data;

namespace Ubp.Infrastructure.Repositories;

public interface IStockReportRepository
{
    Task<PagedResult<StockBalance>> ListBalancesAsync(PageRequest request, Guid? warehouseId,
        bool includeZero, bool isAdmin, IReadOnlyCollection<Guid> branchIds,
        CancellationToken ct = default);
    Task<PagedResult<StockMovement>> ListMovementsAsync(PageRequest request, Guid itemId,
        Guid? warehouseId, DateTime? from, DateTime? to, CancellationToken ct = default);
    Task<decimal> GetOnHandAsync(Guid itemId, Guid warehouseId, CancellationToken ct = default);
}

public sealed class StockReportRepository : IStockReportRepository
{
    private readonly IDbConnectionFactory _factory;

    public StockReportRepository(IDbConnectionFactory factory) => _factory = factory;

    public async Task<PagedResult<StockBalance>> ListBalancesAsync(PageRequest request, Guid? warehouseId,
        bool includeZero, bool isAdmin, IReadOnlyCollection<Guid> branchIds, CancellationToken ct = default)
    {
        var where = new List<string> { "(@warehouseId IS NULL OR b.warehouse_id = @warehouseId)" };
        if (!includeZero) where.Add("b.on_hand <> 0");
        if (!string.IsNullOrWhiteSpace(request.Search))
            where.Add("i.search_text LIKE '%' || normalize_search(@search) || '%'");
        if (!isAdmin)
            where.Add("EXISTS (SELECT 1 FROM warehouse_branches wb WHERE wb.warehouse_id = b.warehouse_id AND wb.branch_id = ANY(@branchIds))");

        var whereSql = "WHERE " + string.Join(" AND ", where);
        var order = request.SortBy?.ToLowerInvariant() switch
        {
            "item_name" => "i.name_ar",
            "on_hand" => "b.on_hand",
            "value" => "b.inventory_value",
            _ => "i.code"
        } + (request.Descending ? " DESC" : " ASC") + ", b.item_id, b.warehouse_id";

        var sql = $"""
            SELECT b.item_id AS ItemId, i.code AS ItemCode, i.name_ar AS ItemName,
                   b.warehouse_id AS WarehouseId, w.name_ar AS WarehouseName,
                   b.on_hand AS OnHand, b.average_cost AS AverageCost,
                   b.inventory_value AS InventoryValue, b.updated_at AS UpdatedAt
            FROM stock_balances b
            JOIN items i ON i.id = b.item_id
            JOIN warehouses w ON w.id = b.warehouse_id
            {whereSql}
            ORDER BY {order} LIMIT @take OFFSET @offset;
            SELECT count(*) FROM stock_balances b JOIN items i ON i.id = b.item_id {whereSql};
            """;

        await using var connection = await _factory.OpenAsync(ct);
        using var multi = await connection.QueryMultipleAsync(new CommandDefinition(sql, new
        {
            warehouseId, search = request.Search, branchIds = branchIds.ToArray(),
            take = request.SafePageSize, offset = request.Offset
        }, cancellationToken: ct));
        var items = (await multi.ReadAsync<StockBalance>()).ToList();
        var total = await multi.ReadFirstAsync<int>();
        return new PagedResult<StockBalance>
        {
            Items = items, Total = total, Page = request.SafePage, PageSize = request.SafePageSize
        };
    }

    public async Task<PagedResult<StockMovement>> ListMovementsAsync(PageRequest request, Guid itemId,
        Guid? warehouseId, DateTime? from, DateTime? to, CancellationToken ct = default)
    {
        var where = new List<string> { "m.item_id = @itemId" };
        if (warehouseId.HasValue) where.Add("m.warehouse_id = @warehouseId");
        if (from.HasValue) where.Add("m.movement_date >= @from");
        if (to.HasValue) where.Add("m.movement_date <= @to");
        var whereSql = "WHERE " + string.Join(" AND ", where);

        var sql = $"""
            SELECT m.id AS Id, m.document_id AS DocumentId, d.doc_no AS DocNo, d.doc_type AS DocType,
                   m.item_id AS ItemId, i.code AS ItemCode, i.name_ar AS ItemName,
                   m.warehouse_id AS WarehouseId, w.name_ar AS WarehouseName,
                   (SELECT b.name_ar FROM branches b WHERE b.id = m.branch_id) AS BranchName,
                   m.direction AS Direction, m.quantity AS Quantity, m.unit_cost AS UnitCost,
                   m.total_cost AS TotalCost, m.movement_date AS MovementDate,
                   m.created_at AS CreatedAt
            FROM stock_movements m
            JOIN stock_documents d ON d.id = m.document_id
            JOIN items i ON i.id = m.item_id
            JOIN warehouses w ON w.id = m.warehouse_id
            {whereSql}
            ORDER BY m.id DESC LIMIT @take OFFSET @offset;
            SELECT count(*) FROM stock_movements m {whereSql};
            """;

        await using var connection = await _factory.OpenAsync(ct);
        using var multi = await connection.QueryMultipleAsync(new CommandDefinition(sql, new
        {
            itemId, warehouseId, from, to, take = request.SafePageSize, offset = request.Offset
        }, cancellationToken: ct));
        var items = (await multi.ReadAsync<StockMovement>()).ToList();
        var total = await multi.ReadFirstAsync<int>();
        return new PagedResult<StockMovement>
        {
            Items = items, Total = total, Page = request.SafePage, PageSize = request.SafePageSize
        };
    }

    public async Task<decimal> GetOnHandAsync(Guid itemId, Guid warehouseId,
        CancellationToken ct = default)
    {
        await using var connection = await _factory.OpenAsync(ct);
        return await connection.ExecuteScalarAsync<decimal?>(new CommandDefinition(
            "SELECT on_hand FROM stock_balances WHERE item_id = @itemId AND warehouse_id = @warehouseId",
            new { itemId, warehouseId }, cancellationToken: ct)) ?? 0;
    }
}
