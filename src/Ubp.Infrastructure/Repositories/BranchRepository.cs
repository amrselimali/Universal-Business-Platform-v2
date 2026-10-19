using Dapper;
using Ubp.Core.Models;
using Ubp.Core.Paging;
using Ubp.Infrastructure.Data;

namespace Ubp.Infrastructure.Repositories;

public interface IBranchRepository
{
    Task<PagedResult<Branch>> ListAsync(PageRequest request, short scope, Guid actorId,
        IReadOnlyCollection<Guid> branchIds, bool isAdmin, CancellationToken ct = default);
    Task<IReadOnlyList<Branch>> ListOptionsAsync(IReadOnlyCollection<Guid> branchIds, bool isAdmin,
        CancellationToken ct = default);
    Task<Branch?> GetAsync(Guid id, CancellationToken ct = default);
    Task<Branch> CreateAsync(BranchInput input, CancellationToken ct = default);
    Task<Branch?> UpdateAsync(Guid id, int version, BranchInput input, CancellationToken ct = default);
    Task<bool> DeleteAsync(Guid id, CancellationToken ct = default);
    Task<Branch?> SetActiveAsync(Guid id, bool isActive, CancellationToken ct = default);
    Task<(int Users, int Warehouses)> CountLinksAsync(Guid id, CancellationToken ct = default);
}

public sealed partial class BranchRepository : IBranchRepository
{
    private readonly IDbConnectionFactory _factory;

    private static readonly IReadOnlyDictionary<string, string> SortMap =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["code"] = "b.code", ["name_ar"] = "b.name_ar", ["name_en"] = "b.name_en",
            ["is_active"] = "b.is_active", ["created_at"] = "b.created_at"
        };

    private const string SelectColumns = """
        b.id AS Id, b.code AS Code, b.name_ar AS NameAr, b.name_en AS NameEn,
        b.address AS Address, b.phone AS Phone, b.is_active AS IsActive,
        b.custom_fields::text AS CustomFieldsJson,
        b.created_at AS CreatedAt, b.updated_at AS UpdatedAt, b.version AS Version
        """;

    public BranchRepository(IDbConnectionFactory factory) => _factory = factory;

    public async Task<PagedResult<Branch>> ListAsync(PageRequest request, short scope, Guid actorId,
        IReadOnlyCollection<Guid> branchIds, bool isAdmin, CancellationToken ct = default)
    {
        var where = new List<string>();
        if (!request.IncludeInactive) where.Add("b.is_active");
        if (!isAdmin) where.Add("b.id = ANY(@branchIds)");
        if (!string.IsNullOrWhiteSpace(request.Search))
            where.Add("b.search_text LIKE '%' || normalize_search(@search) || '%'");

        var whereSql = where.Count == 0 ? "WHERE true" : "WHERE " + string.Join(" AND ", where);
        var order = SqlSort.Build(request.SortBy, SortMap, "b.code", request.Descending);

        var sql = $"""
            SELECT {SelectColumns} FROM branches b {whereSql}
            ORDER BY {order} LIMIT @take OFFSET @offset;
            SELECT count(*) FROM branches b {whereSql};
            """;

        await using var connection = await _factory.OpenAsync(ct);
        using var multi = await connection.QueryMultipleAsync(new CommandDefinition(sql, new
        {
            search = request.Search,
            branchIds = branchIds.ToArray(),
            take = request.SafePageSize,
            offset = request.Offset
        }, cancellationToken: ct));

        var rows = (await multi.ReadAsync<BranchRow>()).ToList();
        var total = await multi.ReadFirstAsync<int>();
        return new PagedResult<Branch>
        {
            Items = rows.Select(Map).ToList(),
            Total = total,
            Page = request.SafePage,
            PageSize = request.SafePageSize
        };
    }

    public async Task<IReadOnlyList<Branch>> ListOptionsAsync(IReadOnlyCollection<Guid> branchIds,
        bool isAdmin, CancellationToken ct = default)
    {
        var where = isAdmin ? "WHERE b.is_active" : "WHERE b.is_active AND b.id = ANY(@branchIds)";
        var sql = $"SELECT {SelectColumns} FROM branches b {where} ORDER BY b.name_ar";
        await using var connection = await _factory.OpenAsync(ct);
        var rows = await connection.QueryAsync<BranchRow>(
            new CommandDefinition(sql, new { branchIds = branchIds.ToArray() }, cancellationToken: ct));
        return rows.Select(Map).ToList();
    }
}
