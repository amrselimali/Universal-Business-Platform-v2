using Dapper;
using Ubp.Core.Models;
using Ubp.Infrastructure.Data;

namespace Ubp.Infrastructure.Repositories;

public sealed partial class RoleRepository
{
    public async Task<Guid> CreateAsync(RoleInput input, CancellationToken ct = default)
    {
        const string sql = """
            INSERT INTO roles (code, name_ar, name_en, is_active)
            VALUES (@Code, @NameAr, @NameEn, @IsActive) RETURNING id
            """;
        await using var connection = await _factory.OpenAsync(ct);
        await using var tx = await connection.BeginTransactionAsync(ct);
        var id = await connection.ExecuteScalarAsync<Guid>(sql, input, tx);
        await ReplacePermissionsAsync(connection, tx, id, input.Permissions, ct);
        await tx.CommitAsync(ct);
        return id;
    }

    public async Task<bool> UpdateAsync(Guid id, int version, RoleInput input, CancellationToken ct = default)
    {
        const string sql = """
            UPDATE roles SET code = @Code, name_ar = @NameAr, name_en = @NameEn, is_active = @IsActive
            WHERE id = @id AND version = @version RETURNING id
            """;
        await using var connection = await _factory.OpenAsync(ct);
        await using var tx = await connection.BeginTransactionAsync(ct);
        var updated = await connection.ExecuteScalarAsync<Guid?>(sql, new
        {
            id, version, input.Code, input.NameAr, input.NameEn, input.IsActive
        }, tx);
        if (updated is null)
        {
            await tx.RollbackAsync(ct);
            return false;
        }
        await ReplacePermissionsAsync(connection, tx, id, input.Permissions, ct);
        await tx.CommitAsync(ct);
        return true;
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        await using var connection = await _factory.OpenAsync(ct);
        return await connection.ExecuteAsync(
            new CommandDefinition("DELETE FROM roles WHERE id = @id", new { id }, cancellationToken: ct)) > 0;
    }

    public async Task<bool> SetActiveAsync(Guid id, bool isActive, CancellationToken ct = default)
    {
        await using var connection = await _factory.OpenAsync(ct);
        return await connection.ExecuteAsync(new CommandDefinition(
            "UPDATE roles SET is_active = @isActive WHERE id = @id", new { id, isActive }, cancellationToken: ct)) > 0;
    }

    internal static async Task ReplacePermissionsAsync(System.Data.Common.DbConnection connection,
        System.Data.Common.DbTransaction tx, Guid roleId, IEnumerable<PermissionEntry> permissions,
        CancellationToken ct)
    {
        await connection.ExecuteAsync("DELETE FROM role_permissions WHERE role_id = @roleId",
            new { roleId }, tx);
        foreach (var permission in permissions.Where(p => p.IsAllowed))
        {
            await connection.ExecuteAsync("""
                INSERT INTO role_permissions (role_id, screen_code, action_code, is_allowed, scope)
                VALUES (@roleId, @screenCode, @actionCode, true, @scope)
                ON CONFLICT (role_id, screen_code, action_code)
                DO UPDATE SET is_allowed = true, scope = EXCLUDED.scope
                """, new { roleId, screenCode = permission.ScreenCode,
                           actionCode = permission.ActionCode, scope = permission.Scope }, tx);
        }
    }
}
