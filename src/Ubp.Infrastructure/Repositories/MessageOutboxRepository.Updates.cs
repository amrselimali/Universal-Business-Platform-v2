using Dapper;
using Ubp.Core.Models;
using Ubp.Infrastructure.Data;

namespace Ubp.Infrastructure.Repositories;

public sealed partial class MessageOutboxRepository
{
    public async Task MarkSentAsync(Guid id, CancellationToken ct = default)
    {
        await using var connection = await _factory.OpenAsync(ct);
        await connection.ExecuteAsync(new CommandDefinition("""
            UPDATE message_outbox
            SET status = 'SENT', sent_at = now(), last_error = NULL
            WHERE id = @id
            """, new { id }, cancellationToken: ct));
    }

    public async Task MarkFailedAsync(Guid id, short attempts, DateTimeOffset nextAttemptAt,
        string error, short maxAttempts, CancellationToken ct = default)
    {
        await using var connection = await _factory.OpenAsync(ct);
        await connection.ExecuteAsync(new CommandDefinition("""
            UPDATE message_outbox SET
                attempts = @attempts,
                last_error = @error,
                status = CASE WHEN @attempts >= @maxAttempts THEN 'FAILED' ELSE 'PENDING' END,
                next_attempt_at = CASE WHEN @attempts >= @maxAttempts
                                       THEN next_attempt_at ELSE @nextAttemptAt END
            WHERE id = @id
            """, new { id, attempts, error = Truncate(error), nextAttemptAt, maxAttempts },
            cancellationToken: ct));
    }

    public async Task<int> CountByStatusAsync(string status, CancellationToken ct = default)
    {
        await using var connection = await _factory.OpenAsync(ct);
        return await connection.ExecuteScalarAsync<int>(new CommandDefinition(
            "SELECT count(*)::int FROM message_outbox WHERE status = @status", new { status },
            cancellationToken: ct));
    }

    public async Task<OutboxMessage?> GetAsync(Guid id, CancellationToken ct = default)
    {
        await using var connection = await _factory.OpenAsync(ct);
        return await connection.QuerySingleOrDefaultAsync<OutboxMessage>(new CommandDefinition(
            $"SELECT {Columns} FROM message_outbox WHERE id = @id", new { id },
            cancellationToken: ct));
    }

    private static string Truncate(string? error) =>
        error is null ? "" : error.Length > 1000 ? error[..1000] : error;
}
