using Dapper;
using Ubp.Core.Models;
using Ubp.Infrastructure.Data;

namespace Ubp.Infrastructure.Repositories;

public interface ISessionRepository
{
    Task<Guid> CreateSessionAsync(Guid userId, Guid? branchId, Guid? refreshTokenId,
        string? ipAddress, DateTimeOffset expiresAt, CancellationToken ct = default);
    Task<UserSession?> GetSessionAsync(Guid id, CancellationToken ct = default);
    Task TouchSessionAsync(Guid id, DateTimeOffset expiresAt, CancellationToken ct = default);
    Task SetSessionBranchAsync(Guid id, Guid? branchId, DateTimeOffset expiresAt, CancellationToken ct = default);
    Task RevokeSessionAsync(Guid id, CancellationToken ct = default);
    Task RevokeAllSessionsAsync(Guid userId, CancellationToken ct = default);
}

public sealed class SessionRepository : ISessionRepository
{
    private readonly IDbConnectionFactory _factory;

    public SessionRepository(IDbConnectionFactory factory) => _factory = factory;

    public async Task<Guid> CreateSessionAsync(Guid userId, Guid? branchId, Guid? refreshTokenId,
        string? ipAddress, DateTimeOffset expiresAt, CancellationToken ct = default)
    {
        const string sql = """
            INSERT INTO user_sessions (user_id, branch_id, refresh_token_id, ip_address, expires_at)
            VALUES (@userId, @branchId, @refreshTokenId, CAST(@ipAddress AS inet), @expiresAt)
            RETURNING id
            """;
        await using var connection = await _factory.OpenAsync(ct);
        return await connection.ExecuteScalarAsync<Guid>(new CommandDefinition(sql,
            new { userId, branchId, refreshTokenId, ipAddress, expiresAt }, cancellationToken: ct));
    }

    public async Task<UserSession?> GetSessionAsync(Guid id, CancellationToken ct = default)
    {
        const string sql = """
            SELECT id AS Id, user_id AS UserId, branch_id AS BranchId,
                   refresh_token_id AS RefreshTokenId, host(ip_address) AS IpAddress,
                   last_seen_at AS LastSeenAt, expires_at AS ExpiresAt, revoked_at AS RevokedAt,
                   created_at AS CreatedAt
            FROM user_sessions WHERE id = @id
            """;
        await using var connection = await _factory.OpenAsync(ct);
        return await connection.QuerySingleOrDefaultAsync<UserSession>(
            new CommandDefinition(sql, new { id }, cancellationToken: ct));
    }

    public async Task TouchSessionAsync(Guid id, DateTimeOffset expiresAt, CancellationToken ct = default)
    {
        await using var connection = await _factory.OpenAsync(ct);
        await connection.ExecuteAsync(new CommandDefinition(
            "UPDATE user_sessions SET last_seen_at = now(), expires_at = @expiresAt WHERE id = @id",
            new { id, expiresAt }, cancellationToken: ct));
    }

    public async Task SetSessionBranchAsync(Guid id, Guid? branchId, DateTimeOffset expiresAt,
        CancellationToken ct = default)
    {
        await using var connection = await _factory.OpenAsync(ct);
        await connection.ExecuteAsync(new CommandDefinition(
            "UPDATE user_sessions SET branch_id = @branchId, expires_at = @expiresAt WHERE id = @id",
            new { id, branchId, expiresAt }, cancellationToken: ct));
    }

    public async Task RevokeSessionAsync(Guid id, CancellationToken ct = default)
    {
        await using var connection = await _factory.OpenAsync(ct);
        await connection.ExecuteAsync(new CommandDefinition(
            "UPDATE user_sessions SET revoked_at = now() WHERE id = @id", new { id }, cancellationToken: ct));
    }

    public async Task RevokeAllSessionsAsync(Guid userId, CancellationToken ct = default)
    {
        await using var connection = await _factory.OpenAsync(ct);
        await connection.ExecuteAsync(new CommandDefinition(
            "UPDATE user_sessions SET revoked_at = now() WHERE user_id = @userId AND revoked_at IS NULL",
            new { userId }, cancellationToken: ct));
    }
}
