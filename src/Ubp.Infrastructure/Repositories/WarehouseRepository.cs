using Dapper;
using Ubp.Core.Models;
using Ubp.Core.Paging;
using Ubp.Infrastructure.Data;

namespace Ubp.Infrastructure.Repositories;

public interface IWarehouseRepository
{
    Task<PagedResult<Warehouse>> ListAsync(PageRequest request, short scope, Guid actorId,
        IReadOnlyCollection<Guid> branchIds, bool isAdmin, CancellationToken ct = default);
    Task<IReadOnlyList<Warehouse>> ListOptionsAsync(short scope, Guid actorId,
        IReadOnlyCollection<Guid> branchIds, bool isAdmin, CancellationToken ct = default);
    Task<Warehouse?> GetAsync(Guid id, CancellationToken ct = default);
    Task<Warehouse> CreateAsync(WarehouseInput input, CancellationToken ct = default);
    Task<Warehouse?> UpdateAsync(Guid id, int version, WarehouseInput input, CancellationToken ct = default);
    Task<Warehouse?> SetActiveAsync(Guid id, bool isActive, CancellationToken ct = default);
    Task<bool> DeleteAsync(Guid id, CancellationToken ct = default);
    Task<(int Users, int Branches)> CountLinksAsync(Guid id, CancellationToken ct = default);
}

public sealed partial class WarehouseRepository : IWarehouseRepository
{
    private readonly IDbConnectionFactory _factory;

    private static readonly IReadOnlyDictionary<string, string> SortMap =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["code"] = "w.code", ["name_ar"] = "w.name_ar", ["name_en"] = "w.name_en",
            ["is_active"] = "w.is_active", ["created_at"] = "w.created_at"
        };

    private const string SelectColumns = """
        w.id AS Id, w.code AS Code, w.name_ar AS NameAr, w.name_en AS NameEn,
        w.is_active AS IsActive, w.custom_fields::text AS CustomFieldsJson,
        coalesce(array_agg(wb.branch_id) FILTER (WHERE wb.branch_id IS NOT NULL), '{}') AS BranchIds,
        w.created_at AS CreatedAt, w.updated_at AS UpdatedAt, w.version AS Version
        """;

    private const string Joins = "LEFT JOIN warehouse_branches wb ON wb.warehouse_id = w.id";

    public WarehouseRepository(IDbConnectionFactory factory) => _factory = factory;

    private static string ScopeWhere(short scope, bool isAdmin, List<string> where)
    {
        if (isAdmin) return "";
        if (scope >= 2) return "";
        if (scope == 1)
        {
            where.Add("EXISTS (SELECT 1 FROM warehouse_branches wbx WHERE wbx.warehouse_id = w.id AND wbx.branch_id = ANY(@branchIds))");
            return "";
        }
        where.Add("EXISTS (SELECT 1 FROM user_warehouses uw WHERE uw.warehouse_id = w.id AND uw.user_id = @actorId)");
        return "";
    }

    public async Task<PagedResult<Warehouse>> ListAsync(PageRequest request, short scope, Guid actorId,
        IReadOnlyCollection<Guid> branchIds, bool isAdmin, CancellationToken ct = default)
    {
        var where = new List<string>();
        if (!request.IncludeInactive) where.Add("w.is_active");
        ScopeWhere(scope, isAdmin, where);
        if (!string.IsNullOrWhiteSpace(request.Search))
            where.Add("w.search_text LIKE '%' || normalize_search(@search) || '%'");

        var whereSql = where.Count == 0 ? "WHERE true" : "WHERE " + string.Join(" AND ", where);
        var order = SqlSort.Build(request.SortBy, SortMap, "w.code", request.Descending);

        var sql = $"""
            SELECT {SelectColumns} FROM warehouses w {Joins} {whereSql}
            GROUP BY w.id
            ORDER BY {order} LIMIT @take OFFSET @offset;
            SELECT count(*) FROM warehouses w WHERE {(where.Count == 0 ? "true" : string.Join(" AND ", where))};
            """;

        await using var connection = await _factory.OpenAsync(ct);
        using var multi = await connection.QueryMultipleAsync(new CommandDefinition(sql, new
        {
            search = request.Search,
            actorId,
            branchIds = branchIds.ToArray(),
            take = request.SafePageSize,
            offset = request.Offset
        }, cancellationToken: ct));

        var rows = (await multi.ReadAsync<WarehouseRow>()).ToList();
        var total = await multi.ReadFirstAsync<int>();
        return new PagedResult<Warehouse>
        {
            Items = rows.Select(Map).ToList(),
            Total = total,
            Page = request.SafePage,
            PageSize = request.SafePageSize
        };
    }

    public async Task<IReadOnlyList<Warehouse>> ListOptionsAsync(short scope, Guid actorId,
        IReadOnlyCollection<Guid> branchIds, bool isAdmin, CancellationToken ct = default)
    {
        var where = new List<string> { "w.is_active" };
        ScopeWhere(scope, isAdmin, where);
        var sql = $"""
            SELECT {SelectColumns} FROM warehouses w {Joins}
            WHERE {string.Join(" AND ", where)}
            GROUP BY w.id ORDER BY w.name_ar
            """;
        await using var connection = await _factory.OpenAsync(ct);
        var rows = await connection.QueryAsync<WarehouseRow>(new CommandDefinition(sql, new
        {
            actorId, branchIds = branchIds.ToArray()
        }, cancellationToken: ct));
        return rows.Select(Map).ToList();
    }
}
