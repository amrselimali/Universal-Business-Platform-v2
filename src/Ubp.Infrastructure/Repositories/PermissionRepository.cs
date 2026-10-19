using Dapper;
using Ubp.Core.Models;
using Ubp.Infrastructure.Data;

namespace Ubp.Infrastructure.Repositories;

public interface IPermissionRepository
{
    Task<IReadOnlyList<PermissionEntry>> GetRoleGrantsForUserAsync(Guid userId, CancellationToken ct = default);
    Task<IReadOnlyList<PermissionEntry>> GetUserOverridesAsync(Guid userId, CancellationToken ct = default);
    Task ReplaceUserPermissionsAsync(Guid userId, IEnumerable<PermissionEntry> permissions,
        CancellationToken ct = default);
    Task CopyUserPermissionsAsync(Guid sourceUserId, Guid targetUserId, CancellationToken ct = default);
}

public sealed class PermissionRepository : IPermissionRepository
{
    private readonly IDbConnectionFactory _factory;

    public PermissionRepository(IDbConnectionFactory factory) => _factory = factory;

    public async Task<IReadOnlyList<PermissionEntry>> GetRoleGrantsForUserAsync(Guid userId,
        CancellationToken ct = default)
    {
        const string sql = """
            SELECT rp.screen_code AS ScreenCode, rp.action_code AS ActionCode,
                   rp.is_allowed AS IsAllowed, rp.scope AS Scope
            FROM role_permissions rp
            JOIN user_roles ur ON ur.role_id = rp.role_id
            JOIN roles r ON r.id = rp.role_id
            WHERE ur.user_id = @userId AND r.is_active AND rp.is_allowed
            """;
        await using var connection = await _factory.OpenAsync(ct);
        var rows = await connection.QueryAsync<PermissionEntry>(
            new CommandDefinition(sql, new { userId }, cancellationToken: ct));
        return rows.ToList();
    }

    public async Task<IReadOnlyList<PermissionEntry>> GetUserOverridesAsync(Guid userId,
        CancellationToken ct = default)
    {
        const string sql = """
            SELECT screen_code AS ScreenCode, action_code AS ActionCode,
                   is_allowed AS IsAllowed, scope AS Scope
            FROM user_permissions WHERE user_id = @userId
            """;
        await using var connection = await _factory.OpenAsync(ct);
        var rows = await connection.QueryAsync<PermissionEntry>(
            new CommandDefinition(sql, new { userId }, cancellationToken: ct));
        return rows.ToList();
    }

    public async Task ReplaceUserPermissionsAsync(Guid userId, IEnumerable<PermissionEntry> permissions,
        CancellationToken ct = default)
    {
        await using var connection = await _factory.OpenAsync(ct);
        await using var tx = await connection.BeginTransactionAsync(ct);
        await connection.ExecuteAsync("DELETE FROM user_permissions WHERE user_id = @userId",
            new { userId }, tx);
        foreach (var p in permissions)
        {
            await connection.ExecuteAsync("""
                INSERT INTO user_permissions (user_id, screen_code, action_code, is_allowed, scope)
                VALUES (@userId, @screenCode, @actionCode, @isAllowed, @scope)
                ON CONFLICT (user_id, screen_code, action_code)
                DO UPDATE SET is_allowed = EXCLUDED.is_allowed, scope = EXCLUDED.scope
                """, new { userId, screenCode = p.ScreenCode, actionCode = p.ActionCode,
                           isAllowed = p.IsAllowed, scope = p.Scope }, tx);
        }
        await tx.CommitAsync(ct);
    }

    public async Task CopyUserPermissionsAsync(Guid sourceUserId, Guid targetUserId,
        CancellationToken ct = default)
    {
        const string sql = """
            INSERT INTO user_permissions (user_id, screen_code, action_code, is_allowed, scope)
            SELECT @targetUserId, screen_code, action_code, is_allowed, scope
            FROM user_permissions WHERE user_id = @sourceUserId
            ON CONFLICT (user_id, screen_code, action_code)
            DO UPDATE SET is_allowed = EXCLUDED.is_allowed, scope = EXCLUDED.scope
            """;
        await using var connection = await _factory.OpenAsync(ct);
        await connection.ExecuteAsync(new CommandDefinition(sql, new { sourceUserId, targetUserId }, cancellationToken: ct));
    }
}
