using Dapper;
using Ubp.Infrastructure.Data;

namespace Ubp.Infrastructure.Repositories;

public sealed partial class UserRepository
{
    public async Task<bool> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        await using var connection = await _factory.OpenAsync(ct);
        return await connection.ExecuteAsync(
            new CommandDefinition("DELETE FROM users WHERE id = @id", new { id }, cancellationToken: ct)) > 0;
    }

    public async Task<bool> SetActiveAsync(Guid id, bool isActive, CancellationToken ct = default)
    {
        await using var connection = await _factory.OpenAsync(ct);
        return await connection.ExecuteAsync(new CommandDefinition(
            "UPDATE users SET is_active = @isActive WHERE id = @id", new { id, isActive }, cancellationToken: ct)) > 0;
    }

    public async Task ResetPasswordAsync(Guid id, string passwordHash, CancellationToken ct = default)
    {
        await using var connection = await _factory.OpenAsync(ct);
        await connection.ExecuteAsync(new CommandDefinition("""
            UPDATE users SET password_hash = @passwordHash, failed_attempts = 0, locked_until = NULL
            WHERE id = @id
            """, new { id, passwordHash }, cancellationToken: ct));
    }

    public async Task UnlockAsync(Guid id, CancellationToken ct = default)
    {
        await using var connection = await _factory.OpenAsync(ct);
        await connection.ExecuteAsync(new CommandDefinition(
            "UPDATE users SET failed_attempts = 0, locked_until = NULL WHERE id = @id",
            new { id }, cancellationToken: ct));
    }

    public async Task UpdateLanguageAsync(Guid id, string language, CancellationToken ct = default)
    {
        await using var connection = await _factory.OpenAsync(ct);
        await connection.ExecuteAsync(new CommandDefinition(
            "UPDATE users SET language = @language WHERE id = @id",
            new { id, language = language == "en" ? "en" : "ar" }, cancellationToken: ct));
    }

    public async Task<bool> HasHistoryAsync(Guid id, CancellationToken ct = default)
    {
        await using var connection = await _factory.OpenAsync(ct);
        return await connection.ExecuteScalarAsync<bool>(new CommandDefinition(
            "SELECT EXISTS (SELECT 1 FROM audit_log WHERE user_id = @id)",
            new { id }, cancellationToken: ct));
    }

    public async Task<(short FailedAttempts, DateTimeOffset? LockedUntil)> RegisterFailedLoginAsync(
        Guid id, short maxAttempts, short lockoutMinutes, CancellationToken ct = default)
    {
        const string sql = """
            UPDATE users SET
                failed_attempts = failed_attempts + 1,
                locked_until = CASE
                    WHEN failed_attempts + 1 >= @maxAttempts
                    THEN now() + make_interval(mins => @lockoutMinutes)
                    ELSE locked_until END
            WHERE id = @id
            RETURNING failed_attempts AS FailedAttempts, locked_until AS LockedUntil
            """;
        await using var connection = await _factory.OpenAsync(ct);
        return await connection.QuerySingleAsync<(short, DateTimeOffset?)>(new CommandDefinition(
            sql, new { id, maxAttempts, lockoutMinutes }, cancellationToken: ct));
    }

    public async Task RegisterSuccessfulLoginAsync(Guid id, CancellationToken ct = default)
    {
        await using var connection = await _factory.OpenAsync(ct);
        await connection.ExecuteAsync(new CommandDefinition("""
            UPDATE users SET failed_attempts = 0, locked_until = NULL, last_login_at = now()
            WHERE id = @id
            """, new { id }, cancellationToken: ct));
    }

    public async Task<int> CountActiveSuperAdminsAsync(CancellationToken ct = default)
    {
        await using var connection = await _factory.OpenAsync(ct);
        return await connection.ExecuteScalarAsync<int>(new CommandDefinition(
            "SELECT count(*)::int FROM users WHERE is_super_admin AND is_active",
            cancellationToken: ct));
    }
}
