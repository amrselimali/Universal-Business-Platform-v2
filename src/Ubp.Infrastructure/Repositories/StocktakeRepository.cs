using Dapper;
using Ubp.Core.Models;
using Ubp.Core.Paging;
using Ubp.Infrastructure.Data;

namespace Ubp.Infrastructure.Repositories;

public interface IStocktakeRepository
{
    Task<PagedResult<Stocktake>> ListAsync(PageRequest request, string? status, bool isAdmin,
        IReadOnlyCollection<Guid> branchIds, CancellationToken ct = default);
    Task<Stocktake?> GetAsync(Guid id, CancellationToken ct = default);
    Task<StocktakeDetail?> GetDetailAsync(Guid id, CancellationToken ct = default);
    Task<string> NextCodeAsync(Guid branchId, DateTime date, CancellationToken ct = default);
    Task<Stocktake> CreateAsync(StocktakeInput input, string code, Guid userId, CancellationToken ct = default);
    Task UpdateLinesAsync(Guid id, IReadOnlyList<StocktakeLineInput> lines, CancellationToken ct = default);
    Task<bool> DeleteAsync(Guid id, CancellationToken ct = default);
    Task MarkPostedAsync(Guid id, Guid documentId, Guid userId, CancellationToken ct = default);
}

public sealed partial class StocktakeRepository : IStocktakeRepository
{
    private static readonly IReadOnlyDictionary<string, string> SortMap =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["code"] = "s.code", ["created_at"] = "s.created_at", ["status"] = "s.status"
        };

    private const string Columns = """
        s.id AS Id, s.code AS Code, s.branch_id AS BranchId,
        (SELECT b.name_ar FROM branches b WHERE b.id = s.branch_id) AS BranchName,
        s.warehouse_id AS WarehouseId,
        (SELECT w.name_ar FROM warehouses w WHERE w.id = s.warehouse_id) AS WarehouseName,
        s.status AS Status, s.notes AS Notes, s.posted_at AS PostedAt,
        s.created_at AS CreatedAt, s.updated_at AS UpdatedAt, s.version AS Version,
        (SELECT count(*)::int FROM stocktake_lines l WHERE l.stocktake_id = s.id) AS LinesCount,
        COALESCE((SELECT sum(l.difference) FROM stocktake_lines l WHERE l.stocktake_id = s.id), 0)
            AS TotalDifference,
        (SELECT d.id FROM stock_documents d WHERE d.stocktake_id = s.id LIMIT 1) AS DocumentId
        """;

    private readonly IDbConnectionFactory _factory;

    public StocktakeRepository(IDbConnectionFactory factory) => _factory = factory;

    public async Task<PagedResult<Stocktake>> ListAsync(PageRequest request, string? status,
        bool isAdmin, IReadOnlyCollection<Guid> branchIds, CancellationToken ct = default)
    {
        var where = new List<string>();
        if (!isAdmin) where.Add("s.branch_id = ANY(@branchIds)");
        if (!string.IsNullOrWhiteSpace(status)) where.Add("s.status = @status");
        if (!string.IsNullOrWhiteSpace(request.Search))
            where.Add("(s.code ILIKE '%' || @search || '%' OR coalesce(s.notes,'') ILIKE '%' || @search || '%')");

        var whereSql = where.Count == 0 ? "WHERE true" : "WHERE " + string.Join(" AND ", where);
        var order = SqlSort.Build(request.SortBy, SortMap, "s.created_at", request.Descending);
        var sql = $"""
            SELECT {Columns} FROM stocktakes s {whereSql}
            ORDER BY {order} LIMIT @take OFFSET @offset;
            SELECT count(*) FROM stocktakes s {whereSql};
            """;

        await using var connection = await _factory.OpenAsync(ct);
        using var multi = await connection.QueryMultipleAsync(new CommandDefinition(sql, new
        {
            branchIds = branchIds.ToArray(), status, search = request.Search,
            take = request.SafePageSize, offset = request.Offset
        }, cancellationToken: ct));
        var items = (await multi.ReadAsync<Stocktake>()).ToList();
        var total = await multi.ReadFirstAsync<int>();
        return new PagedResult<Stocktake>
        {
            Items = items, Total = total, Page = request.SafePage, PageSize = request.SafePageSize
        };
    }

    public async Task<string> NextCodeAsync(Guid branchId, DateTime date, CancellationToken ct = default)
    {
        await using var connection = await _factory.OpenAsync(ct);
        return await connection.ExecuteScalarAsync<string>(new CommandDefinition(
            "SELECT next_document_number('STOCKTAKE'::varchar, @branchId::uuid, @date::date, 'STK'::varchar, 4::smallint)",
            new { branchId, date }, cancellationToken: ct)) ?? "STK";
    }

    public async Task<Stocktake?> GetAsync(Guid id, CancellationToken ct = default)
    {
        await using var connection = await _factory.OpenAsync(ct);
        return await connection.QuerySingleOrDefaultAsync<Stocktake>(new CommandDefinition(
            $"SELECT {Columns} FROM stocktakes s WHERE s.id = @id", new { id }, cancellationToken: ct));
    }

    public async Task<StocktakeDetail?> GetDetailAsync(Guid id, CancellationToken ct = default)
    {
        var header = await GetAsync(id, ct);
        if (header is null) return null;

        const string sql = """
            SELECT l.id AS Id, l.stocktake_id AS StocktakeId, l.item_id AS ItemId,
                   (SELECT i.code FROM items i WHERE i.id = l.item_id) AS ItemCode,
                   (SELECT i.name_ar FROM items i WHERE i.id = l.item_id) AS ItemName,
                   l.book_qty AS BookQty, l.counted_qty AS CountedQty, l.difference AS Difference,
                   l.unit_cost AS UnitCost
            FROM stocktake_lines l WHERE l.stocktake_id = @id ORDER BY l.item_id
            """;
        await using var connection = await _factory.OpenAsync(ct);
        var lines = (await connection.QueryAsync<StocktakeLine>(new CommandDefinition(sql, new { id }, cancellationToken: ct))).ToList();
        return new StocktakeDetail { Header = header, Lines = lines };
    }
}
