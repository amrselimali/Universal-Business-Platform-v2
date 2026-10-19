using Dapper;
using Ubp.Core.Models;
using Ubp.Infrastructure.Data;

namespace Ubp.Infrastructure.Repositories;

public sealed partial class UserRepository
{
    public async Task<UserDetail?> GetDetailAsync(Guid id, CancellationToken ct = default)
    {
        const string sql = """
            SELECT u.id AS Id, u.name AS Name, u.username AS Username, u.password_hash AS PasswordHash,
                   u.is_active AS IsActive, u.language AS Language, u.is_admin AS IsAdmin,
                   u.is_super_admin AS IsSuperAdmin, u.failed_attempts AS FailedAttempts,
                   u.locked_until AS LockedUntil, u.last_login_at AS LastLoginAt,
                   u.created_at AS CreatedAt, u.updated_at AS UpdatedAt, u.version AS Version,
                   coalesce((SELECT array_agg(branch_id ORDER BY branch_id) FROM user_branches WHERE user_id = u.id), '{}') AS BranchIds,
                   (SELECT branch_id FROM user_branches WHERE user_id = u.id AND is_default LIMIT 1) AS DefaultBranchId,
                   coalesce((SELECT array_agg(warehouse_id ORDER BY warehouse_id) FROM user_warehouses WHERE user_id = u.id), '{}') AS WarehouseIds,
                   coalesce((SELECT array_agg(role_id ORDER BY role_id) FROM user_roles WHERE user_id = u.id), '{}') AS RoleIds
            FROM users u WHERE u.id = @id
            """;
        await using var connection = await _factory.OpenAsync(ct);
        var row = await connection.QuerySingleOrDefaultAsync<DetailRow>(
            new CommandDefinition(sql, new { id }, cancellationToken: ct));
        if (row is null) return null;
        return new UserDetail
        {
            User = MapUser(row),
            BranchIds = row.BranchIds?.ToList() ?? new(),
            DefaultBranchId = row.DefaultBranchId,
            WarehouseIds = row.WarehouseIds?.ToList() ?? new(),
            RoleIds = row.RoleIds?.ToList() ?? new()
        };
    }

    public async Task<bool> IsUsernameTakenAsync(string username, Guid? exceptId, CancellationToken ct = default)
    {
        await using var connection = await _factory.OpenAsync(ct);
        return await connection.ExecuteScalarAsync<bool>(new CommandDefinition(
            "SELECT EXISTS (SELECT 1 FROM users WHERE lower(username) = lower(@username) AND (@exceptId IS NULL OR id <> @exceptId))",
            new { username, exceptId }, cancellationToken: ct));
    }

    public async Task<IReadOnlyList<Guid>> GetBranchIdsAsync(Guid userId, CancellationToken ct = default)
    {
        await using var connection = await _factory.OpenAsync(ct);
        var rows = await connection.QueryAsync<Guid>(new CommandDefinition(
            "SELECT branch_id FROM user_branches WHERE user_id = @userId ORDER BY is_default DESC, branch_id",
            new { userId }, cancellationToken: ct));
        return rows.ToList();
    }

    public async Task<Guid?> GetDefaultBranchIdAsync(Guid userId, CancellationToken ct = default)
    {
        await using var connection = await _factory.OpenAsync(ct);
        return await connection.ExecuteScalarAsync<Guid?>(new CommandDefinition(
            "SELECT branch_id FROM user_branches WHERE user_id = @userId ORDER BY is_default DESC LIMIT 1",
            new { userId }, cancellationToken: ct));
    }

    public async Task<IReadOnlyList<Guid>> GetWarehouseIdsAsync(Guid userId, CancellationToken ct = default)
    {
        await using var connection = await _factory.OpenAsync(ct);
        var rows = await connection.QueryAsync<Guid>(new CommandDefinition(
            "SELECT warehouse_id FROM user_warehouses WHERE user_id = @userId",
            new { userId }, cancellationToken: ct));
        return rows.ToList();
    }

    private static UserListItem MapList(UserListRow row) => new()
    {
        Id = row.Id, Name = row.Name, Username = row.Username, IsActive = row.IsActive,
        IsAdmin = row.IsAdmin, IsSuperAdmin = row.IsSuperAdmin,
        IsLocked = row.LockedUntil.HasValue && row.LockedUntil > DateTimeOffset.UtcNow,
        LastLoginAt = row.LastLoginAt, CreatedAt = row.CreatedAt, Version = row.Version,
        BranchCount = row.BranchCount, WarehouseCount = row.WarehouseCount
    };

    private static User MapUser(DetailRow row) => new()
    {
        Id = row.Id, Name = row.Name, Username = row.Username, PasswordHash = row.PasswordHash,
        IsActive = row.IsActive, Language = row.Language, IsAdmin = row.IsAdmin,
        IsSuperAdmin = row.IsSuperAdmin, FailedAttempts = row.FailedAttempts,
        LockedUntil = row.LockedUntil, LastLoginAt = row.LastLoginAt,
        CreatedAt = row.CreatedAt, UpdatedAt = row.UpdatedAt, Version = row.Version
    };

    private class UserListRow
    {
        public Guid Id { get; set; }
        public string? Name { get; set; }
        public string Username { get; set; } = "";
        public bool IsActive { get; set; }
        public bool IsAdmin { get; set; }
        public bool IsSuperAdmin { get; set; }
        public DateTimeOffset? LockedUntil { get; set; }
        public DateTimeOffset? LastLoginAt { get; set; }
        public DateTimeOffset CreatedAt { get; set; }
        public int Version { get; set; }
        public int BranchCount { get; set; }
        public int WarehouseCount { get; set; }
    }

    private sealed class DetailRow : UserListRow
    {
        public string PasswordHash { get; set; } = "";
        public string Language { get; set; } = "ar";
        public short FailedAttempts { get; set; }
        public DateTimeOffset UpdatedAt { get; set; }
        public Guid[]? BranchIds { get; set; }
        public Guid? DefaultBranchId { get; set; }
        public Guid[]? WarehouseIds { get; set; }
        public Guid[]? RoleIds { get; set; }
    }
}
