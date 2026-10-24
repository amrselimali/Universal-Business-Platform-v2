using Dapper;
using Ubp.Core.Models;
using Ubp.Core.Paging;
using Ubp.Infrastructure.Data;

namespace Ubp.Infrastructure.Repositories;

public interface ICostCenterRepository
{
    Task<PagedResult<CostCenter>> ListAsync(CostCenterFilter filter, PageRequest request,
        CancellationToken ct = default);
    Task<IReadOnlyList<CostCenter>> ListOptionsAsync(Guid? branchId, CancellationToken ct = default);
    Task<CostCenter?> GetAsync(Guid id, CancellationToken ct = default);
    Task<CostCenter?> GetByCodeAsync(string code, CancellationToken ct = default);
    Task<int> CountUsageAsync(Guid id, CancellationToken ct = default);
    Task<Guid> CreateAsync(Guid id, CostCenterInput input, string path, short level, Guid userId,
        CancellationToken ct = default);
    Task<CostCenter?> UpdateAsync(Guid id, int version, CostCenterInput input, Guid userId,
        CancellationToken ct = default);
    Task<CostCenter?> SetActiveAsync(Guid id, bool isActive, Guid userId, CancellationToken ct = default);
    Task<bool> DeleteAsync(Guid id, CancellationToken ct = default);
    Task<CostCenter?> MoveAsync(Guid id, Guid? newParentId, Guid userId, CancellationToken ct = default);
}

public sealed partial class CostCenterRepository : ICostCenterRepository
{
    private const string Columns = """
        c.id AS Id, c.code AS Code, c.name_ar AS NameAr, c.name_en AS NameEn,
        c.parent_id AS ParentId, c.path AS Path, c.level AS Level, c.is_group AS IsGroup,
        c.branch_id AS BranchId, b.name_ar AS BranchName, c.notes AS Notes,
        c.is_active AS IsActive, c.version AS Version,
        (SELECT count(*)::int FROM cost_centers x WHERE x.parent_id = c.id) AS ChildrenCount
        """;

    private static readonly IReadOnlyDictionary<string, string> SortMap =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["code"] = "c.code", ["name_ar"] = "c.name_ar", ["path"] = "c.path", ["level"] = "c.level"
        };

    private readonly IDbConnectionFactory _factory;

    public CostCenterRepository(IDbConnectionFactory factory) => _factory = factory;

    public async Task<PagedResult<CostCenter>> ListAsync(CostCenterFilter filter, PageRequest request,
        CancellationToken ct = default)
    {
        var where = new List<string>();
        if (!filter.IncludeInactive) where.Add("c.is_active");
        if (filter.IsGroup.HasValue) where.Add("c.is_group = @isGroup");
        if (filter.BranchId.HasValue) where.Add("(c.branch_id IS NULL OR c.branch_id = @branchId)");
        if (!string.IsNullOrWhiteSpace(request.Search)) where.Add(SearchSql.Contains("c.search_text"));

        var whereSql = where.Count == 0 ? "WHERE true" : "WHERE " + string.Join(" AND ", where);
        var order = SqlSort.Build(request.SortBy, SortMap, "c.path", request.Descending);
        var sql = $"""
            SELECT {Columns} FROM cost_centers c
            LEFT JOIN branches b ON b.id = c.branch_id {whereSql}
            ORDER BY {order} LIMIT @take OFFSET @offset;
            SELECT count(*) FROM cost_centers c {whereSql};
            """;

        await using var connection = await _factory.OpenAsync(ct);
        using var multi = await connection.QueryMultipleAsync(new CommandDefinition(sql, new
        {
            search = request.Search, take = request.SafePageSize, offset = request.Offset,
            isGroup = filter.IsGroup, branchId = filter.BranchId
        }, cancellationToken: ct));
        var items = (await multi.ReadAsync<CostCenter>()).ToList();
        var total = await multi.ReadFirstAsync<int>();
        return new PagedResult<CostCenter>
        {
            Items = items, Total = total, Page = request.SafePage, PageSize = request.SafePageSize
        };
    }

    public async Task<IReadOnlyList<CostCenter>> ListOptionsAsync(Guid? branchId,
        CancellationToken ct = default)
    {
        await using var connection = await _factory.OpenAsync(ct);
        var rows = await connection.QueryAsync<CostCenter>(new CommandDefinition(
            $"SELECT {Columns} FROM cost_centers c LEFT JOIN branches b ON b.id = c.branch_id " +
            "WHERE c.is_active AND NOT c.is_group AND (@branchId::uuid IS NULL OR " +
            "c.branch_id IS NULL OR c.branch_id = @branchId::uuid) ORDER BY c.code",
            new { branchId }, cancellationToken: ct));
        return rows.ToList();
    }

    public async Task<CostCenter?> GetAsync(Guid id, CancellationToken ct = default)
    {
        await using var connection = await _factory.OpenAsync(ct);
        return await connection.QuerySingleOrDefaultAsync<CostCenter>(new CommandDefinition(
            $"SELECT {Columns} FROM cost_centers c LEFT JOIN branches b ON b.id = c.branch_id " +
            "WHERE c.id = @id", new { id }, cancellationToken: ct));
    }

    public async Task<CostCenter?> GetByCodeAsync(string code, CancellationToken ct = default)
    {
        await using var connection = await _factory.OpenAsync(ct);
        return await connection.QuerySingleOrDefaultAsync<CostCenter>(new CommandDefinition(
            $"SELECT {Columns} FROM cost_centers c LEFT JOIN branches b ON b.id = c.branch_id " +
            "WHERE c.code = @code", new { code }, cancellationToken: ct));
    }

    public async Task<int> CountUsageAsync(Guid id, CancellationToken ct = default)
    {
        await using var connection = await _factory.OpenAsync(ct);
        return await connection.ExecuteScalarAsync<int>(new CommandDefinition("""
            SELECT (SELECT count(*)::int FROM journal_entry_lines WHERE cost_center_id = @id)
                 + (SELECT count(*)::int FROM cost_centers WHERE parent_id = @id)
            """, new { id }, cancellationToken: ct));
    }
}
