using Dapper;
using Ubp.Core.Models;
using Ubp.Core.Paging;
using Ubp.Infrastructure.Data;

namespace Ubp.Infrastructure.Repositories;

public interface IRoleRepository
{
    Task<PagedResult<Role>> ListAsync(PageRequest request, CancellationToken ct = default);
    Task<IReadOnlyList<Role>> ListOptionsAsync(CancellationToken ct = default);
    Task<Role?> GetAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<PermissionEntry>> GetPermissionsAsync(Guid roleId, CancellationToken ct = default);
    Task<Guid> CreateAsync(RoleInput input, CancellationToken ct = default);
    Task<bool> UpdateAsync(Guid id, int version, RoleInput input, CancellationToken ct = default);
    Task<bool> DeleteAsync(Guid id, CancellationToken ct = default);
    Task<bool> SetActiveAsync(Guid id, bool isActive, CancellationToken ct = default);
    Task<int> CountUsersAsync(Guid roleId, CancellationToken ct = default);
}

public sealed partial class RoleRepository : IRoleRepository
{
    private readonly IDbConnectionFactory _factory;

    private const string Columns = """
        id AS Id, code AS Code, name_ar AS NameAr, name_en AS NameEn, is_active AS IsActive,
        created_at AS CreatedAt, updated_at AS UpdatedAt, version AS Version
        """;

    private static readonly IReadOnlyDictionary<string, string> SortMap =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["code"] = "code", ["name_ar"] = "name_ar", ["name_en"] = "name_en",
            ["is_active"] = "is_active", ["created_at"] = "created_at"
        };

    public RoleRepository(IDbConnectionFactory factory) => _factory = factory;

    public async Task<PagedResult<Role>> ListAsync(PageRequest request, CancellationToken ct = default)
    {
        var where = new List<string>();
        if (!request.IncludeInactive) where.Add("is_active");
        if (!string.IsNullOrWhiteSpace(request.Search))
            where.Add("normalize_search(code || ' ' || name_ar || ' ' || coalesce(name_en,'')) LIKE '%' || normalize_search(@search) || '%'");
        var whereSql = where.Count == 0 ? "WHERE true" : "WHERE " + string.Join(" AND ", where);
        var order = SqlSort.Build(request.SortBy, SortMap, "code", request.Descending);
        var sql = $"""
            SELECT {Columns} FROM roles {whereSql}
            ORDER BY {order} LIMIT @take OFFSET @offset;
            SELECT count(*) FROM roles {whereSql};
            """;
        await using var connection = await _factory.OpenAsync(ct);
        using var multi = await connection.QueryMultipleAsync(new CommandDefinition(sql, new
        {
            search = request.Search, take = request.SafePageSize, offset = request.Offset
        }, cancellationToken: ct));
        var items = (await multi.ReadAsync<Role>()).ToList();
        var total = await multi.ReadFirstAsync<int>();
        return new PagedResult<Role> { Items = items, Total = total, Page = request.SafePage, PageSize = request.SafePageSize };
    }

    public async Task<IReadOnlyList<Role>> ListOptionsAsync(CancellationToken ct = default)
    {
        await using var connection = await _factory.OpenAsync(ct);
        var items = await connection.QueryAsync<Role>(
            new CommandDefinition($"SELECT {Columns} FROM roles WHERE is_active ORDER BY name_ar", cancellationToken: ct));
        return items.ToList();
    }

    public async Task<Role?> GetAsync(Guid id, CancellationToken ct = default)
    {
        await using var connection = await _factory.OpenAsync(ct);
        return await connection.QuerySingleOrDefaultAsync<Role>(
            new CommandDefinition($"SELECT {Columns} FROM roles WHERE id = @id", new { id }, cancellationToken: ct));
    }

    public async Task<IReadOnlyList<PermissionEntry>> GetPermissionsAsync(Guid roleId, CancellationToken ct = default)
    {
        const string sql = """
            SELECT screen_code AS ScreenCode, action_code AS ActionCode,
                   is_allowed AS IsAllowed, scope AS Scope
            FROM role_permissions WHERE role_id = @roleId
            """;
        await using var connection = await _factory.OpenAsync(ct);
        var rows = await connection.QueryAsync<PermissionEntry>(
            new CommandDefinition(sql, new { roleId }, cancellationToken: ct));
        return rows.ToList();
    }

    public async Task<int> CountUsersAsync(Guid roleId, CancellationToken ct = default)
    {
        await using var connection = await _factory.OpenAsync(ct);
        return await connection.ExecuteScalarAsync<int>(new CommandDefinition(
            "SELECT count(*)::int FROM user_roles WHERE role_id = @roleId", new { roleId }, cancellationToken: ct));
    }
}
