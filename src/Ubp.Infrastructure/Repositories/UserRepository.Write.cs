using Dapper;
using Npgsql;
using Ubp.Core.Models;
using Ubp.Infrastructure.Data;

namespace Ubp.Infrastructure.Repositories;

public sealed partial class UserRepository
{
    public async Task<Guid> CreateAsync(UserInput input, string passwordHash, CancellationToken ct = default)
    {
        const string sql = """
            INSERT INTO users (name, username, password_hash, is_active, language, is_admin, is_super_admin)
            VALUES (@Name, @Username, @PasswordHash, @IsActive, @Language, @IsAdmin, @IsSuperAdmin)
            RETURNING id
            """;
        await using var connection = await _factory.OpenAsync(ct);
        await using var tx = await connection.BeginTransactionAsync(ct);
        var id = await connection.ExecuteScalarAsync<Guid>(sql, new
        {
            input.Name, input.Username, PasswordHash = passwordHash, input.IsActive,
            input.Language, input.IsAdmin, input.IsSuperAdmin
        }, tx);
        await ReplaceLinksAsync(connection, tx, id, input, ct);
        await tx.CommitAsync(ct);
        return id;
    }

    public async Task<bool> UpdateAsync(Guid id, int version, UserInput input, string? passwordHash,
        CancellationToken ct = default)
    {
        const string sql = """
            UPDATE users SET
                name = @Name, username = @Username, is_active = @IsActive, language = @Language,
                is_admin = @IsAdmin, is_super_admin = @IsSuperAdmin,
                password_hash = coalesce(@PasswordHash, password_hash)
            WHERE id = @id AND version = @version
            RETURNING id
            """;
        await using var connection = await _factory.OpenAsync(ct);
        await using var tx = await connection.BeginTransactionAsync(ct);
        var updated = await connection.ExecuteScalarAsync<Guid?>(sql, new
        {
            id, version, input.Name, input.Username, input.IsActive, input.Language,
            input.IsAdmin, input.IsSuperAdmin, PasswordHash = passwordHash
        }, tx);
        if (updated is null)
        {
            await tx.RollbackAsync(ct);
            return false;
        }
        await ReplaceLinksAsync(connection, tx, id, input, ct);
        await tx.CommitAsync(ct);
        return true;
    }

    private static async Task ReplaceLinksAsync(NpgsqlConnection connection, NpgsqlTransaction tx,
        Guid userId, UserInput input, CancellationToken ct)
    {
        await connection.ExecuteAsync("DELETE FROM user_warehouses WHERE user_id = @userId",
            new { userId }, tx);
        await connection.ExecuteAsync("DELETE FROM user_branches WHERE user_id = @userId",
            new { userId }, tx);

        var branches = input.BranchIds.Distinct().ToList();
        var defaultBranch = input.DefaultBranchId is { } d && branches.Contains(d)
            ? d : branches.FirstOrDefault();
        foreach (var branchId in branches)
        {
            await connection.ExecuteAsync("""
                INSERT INTO user_branches (user_id, branch_id, is_default)
                VALUES (@userId, @branchId, @isDefault) ON CONFLICT DO NOTHING
                """, new { userId, branchId, isDefault = branchId == defaultBranch }, tx);
        }

        foreach (var warehouseId in input.WarehouseIds.Distinct())
        {
            await connection.ExecuteAsync("""
                INSERT INTO user_warehouses (user_id, warehouse_id)
                VALUES (@userId, @warehouseId) ON CONFLICT DO NOTHING
                """, new { userId, warehouseId }, tx);
        }

        await connection.ExecuteAsync("DELETE FROM user_roles WHERE user_id = @userId", new { userId }, tx);
        foreach (var roleId in input.RoleIds.Distinct())
        {
            await connection.ExecuteAsync(
                "INSERT INTO user_roles (user_id, role_id) VALUES (@userId, @roleId) ON CONFLICT DO NOTHING",
                new { userId, roleId }, tx);
        }
    }
}
