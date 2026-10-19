using Dapper;
using Ubp.Infrastructure.Data;

namespace Ubp.Infrastructure.Repositories;

public interface IRefreshTokenRepository
{
    Task<Guid> CreateAsync(Guid userId, string tokenHash, DateTimeOffset expiresAt,
        string? ipAddress, CancellationToken ct = default);
    Task<(Guid Id, Guid UserId, DateTimeOffset ExpiresAt)?> GetActiveAsync(string tokenHash,
        CancellationToken ct = default);
    Task RevokeAsync(Guid id, Guid? replacedBy, CancellationToken ct = default);
    Task RevokeAllForUserAsync(Guid userId, CancellationToken ct = default);
}

public sealed class RefreshTokenRepository : IRefreshTokenRepository
{
    private readonly IDbConnectionFactory _factory;

    public RefreshTokenRepository(IDbConnectionFactory factory) => _factory = factory;

    public async Task<Guid> CreateAsync(Guid userId, string tokenHash, DateTimeOffset expiresAt,
        string? ipAddress, CancellationToken ct = default)
    {
        const string sql = """
            INSERT INTO refresh_tokens (user_id, token_hash, expires_at, created_ip)
            VALUES (@userId, @tokenHash, @expiresAt, CAST(@ipAddress AS inet))
            RETURNING id
            """;
        await using var connection = await _factory.OpenAsync(ct);
        return await connection.ExecuteScalarAsync<Guid>(new CommandDefinition(sql,
            new { userId, tokenHash, expiresAt, ipAddress }, cancellationToken: ct));
    }

    public async Task<(Guid Id, Guid UserId, DateTimeOffset ExpiresAt)?> GetActiveAsync(
        string tokenHash, CancellationToken ct = default)
    {
        const string sql = """
            SELECT id, user_id, expires_at FROM refresh_tokens
            WHERE token_hash = @tokenHash AND revoked_at IS NULL AND expires_at > now()
            """;
        await using var connection = await _factory.OpenAsync(ct);
        var row = await connection.QuerySingleOrDefaultAsync<(Guid Id, Guid UserId, DateTimeOffset ExpiresAt)>(
            new CommandDefinition(sql, new { tokenHash }, cancellationToken: ct));
        return row.Id == Guid.Empty ? null : row;
    }

    public async Task RevokeAsync(Guid id, Guid? replacedBy, CancellationToken ct = default)
    {
        await using var connection = await _factory.OpenAsync(ct);
        await connection.ExecuteAsync(new CommandDefinition(
            "UPDATE refresh_tokens SET revoked_at = now(), replaced_by = @replacedBy WHERE id = @id AND revoked_at IS NULL",
            new { id, replacedBy }, cancellationToken: ct));
    }

    public async Task RevokeAllForUserAsync(Guid userId, CancellationToken ct = default)
    {
        await using var connection = await _factory.OpenAsync(ct);
        await connection.ExecuteAsync(new CommandDefinition(
            "UPDATE refresh_tokens SET revoked_at = now() WHERE user_id = @userId AND revoked_at IS NULL",
            new { userId }, cancellationToken: ct));
    }
}
