using Dapper;
using Ubp.Core.Models;
using Ubp.Core.Paging;
using Ubp.Infrastructure.Data;

namespace Ubp.Infrastructure.Repositories;

public interface IUserRepository
{
    Task<PagedResult<UserListItem>> ListAsync(PageRequest request, short scope, Guid actorId,
        IReadOnlyCollection<Guid> branchIds, bool isAdmin, bool includeSuperAdmins,
        CancellationToken ct = default);
    Task<User?> GetAuthByUsernameAsync(string username, CancellationToken ct = default);
    Task<User?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<UserDetail?> GetDetailAsync(Guid id, CancellationToken ct = default);
    Task<bool> IsUsernameTakenAsync(string username, Guid? exceptId, CancellationToken ct = default);
    Task<IReadOnlyList<Guid>> GetBranchIdsAsync(Guid userId, CancellationToken ct = default);
    Task<Guid?> GetDefaultBranchIdAsync(Guid userId, CancellationToken ct = default);
    Task<IReadOnlyList<Guid>> GetWarehouseIdsAsync(Guid userId, CancellationToken ct = default);
    Task<Guid> CreateAsync(UserInput input, string passwordHash, CancellationToken ct = default);
    Task<bool> UpdateAsync(Guid id, int version, UserInput input, string? passwordHash, CancellationToken ct = default);
    Task<bool> DeleteAsync(Guid id, CancellationToken ct = default);
    Task<bool> SetActiveAsync(Guid id, bool isActive, CancellationToken ct = default);
    Task ResetPasswordAsync(Guid id, string passwordHash, CancellationToken ct = default);
    Task UpdateLanguageAsync(Guid id, string language, CancellationToken ct = default);
    Task<bool> HasHistoryAsync(Guid id, CancellationToken ct = default);
    Task UnlockAsync(Guid id, CancellationToken ct = default);
    Task<(short FailedAttempts, DateTimeOffset? LockedUntil)> RegisterFailedLoginAsync(
        Guid id, short maxAttempts, short lockoutMinutes, CancellationToken ct = default);
    Task RegisterSuccessfulLoginAsync(Guid id, CancellationToken ct = default);
    Task<int> CountActiveSuperAdminsAsync(CancellationToken ct = default);
}

public sealed partial class UserRepository : IUserRepository
{
    private readonly IDbConnectionFactory _factory;

    private static readonly IReadOnlyDictionary<string, string> SortMap =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["username"] = "u.username", ["name"] = "u.name", ["is_active"] = "u.is_active",
            ["last_login_at"] = "u.last_login_at", ["created_at"] = "u.created_at"
        };

    private const string UserColumns = """
        u.id AS Id, u.name AS Name, u.username AS Username, u.password_hash AS PasswordHash,
        u.is_active AS IsActive, u.language AS Language, u.is_admin AS IsAdmin,
        u.is_super_admin AS IsSuperAdmin, u.failed_attempts AS FailedAttempts,
        u.locked_until AS LockedUntil, u.last_login_at AS LastLoginAt,
        u.created_at AS CreatedAt, u.updated_at AS UpdatedAt, u.version AS Version
        """;

    public UserRepository(IDbConnectionFactory factory) => _factory = factory;

    public async Task<PagedResult<UserListItem>> ListAsync(PageRequest request, short scope, Guid actorId,
        IReadOnlyCollection<Guid> branchIds, bool isAdmin, bool includeSuperAdmins,
        CancellationToken ct = default)
    {
        var where = new List<string> { "(@includeSuper OR NOT u.is_super_admin)" };
        if (!request.IncludeInactive) where.Add("u.is_active");
        if (!isAdmin)
        {
            if (scope >= 2) { }
            else if (scope == 1)
                where.Add("(u.id = @actorId OR EXISTS (SELECT 1 FROM user_branches ub WHERE ub.user_id = u.id AND ub.branch_id = ANY(@branchIds)))");
            else
                where.Add("u.id = @actorId");
        }
        if (!string.IsNullOrWhiteSpace(request.Search))
            where.Add("u.search_text LIKE '%' || normalize_search(@search) || '%'");

        var whereSql = "WHERE " + string.Join(" AND ", where);
        var order = SqlSort.Build(request.SortBy, SortMap, "u.username", request.Descending);
        var sql = $"""
            SELECT {UserColumns},
                   (SELECT count(*)::int FROM user_branches ub WHERE ub.user_id = u.id) AS BranchCount,
                   (SELECT count(*)::int FROM user_warehouses uw WHERE uw.user_id = u.id) AS WarehouseCount
            FROM users u {whereSql}
            ORDER BY {order} LIMIT @take OFFSET @offset;
            SELECT count(*) FROM users u {whereSql};
            """;

        await using var connection = await _factory.OpenAsync(ct);
        using var multi = await connection.QueryMultipleAsync(new CommandDefinition(sql, new
        {
            search = request.Search,
            actorId,
            branchIds = branchIds.ToArray(),
            includeSuper = includeSuperAdmins,
            take = request.SafePageSize,
            offset = request.Offset
        }, cancellationToken: ct));

        var rows = (await multi.ReadAsync<UserListRow>()).ToList();
        var total = await multi.ReadFirstAsync<int>();
        return new PagedResult<UserListItem>
        {
            Items = rows.Select(MapList).ToList(), Total = total,
            Page = request.SafePage, PageSize = request.SafePageSize
        };
    }

    public async Task<User?> GetAuthByUsernameAsync(string username, CancellationToken ct = default)
    {
        await using var connection = await _factory.OpenAsync(ct);
        return await connection.QuerySingleOrDefaultAsync<User>(new CommandDefinition(
            $"SELECT {UserColumns} FROM users u WHERE lower(u.username) = lower(@username)",
            new { username }, cancellationToken: ct));
    }

    public async Task<User?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        await using var connection = await _factory.OpenAsync(ct);
        return await connection.QuerySingleOrDefaultAsync<User>(new CommandDefinition(
            $"SELECT {UserColumns} FROM users u WHERE u.id = @id", new { id }, cancellationToken: ct));
    }
}
