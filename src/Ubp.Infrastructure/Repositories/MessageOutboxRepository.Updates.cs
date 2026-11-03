using Dapper;
using Ubp.Core.Models;
using Ubp.Infrastructure.Data;

namespace Ubp.Infrastructure.Repositories;

public sealed partial class MessageOutboxRepository
{
    public async Task MarkSentAsync(Guid id, CancellationToken ct = default) =>
        await MarkSentAsync(id, null, ct);

    public async Task MarkSentAsync(Guid id, string? providerMessageId,
        CancellationToken ct = default)
    {
        await using var connection = await _factory.OpenAsync(ct);
        await connection.ExecuteAsync(new CommandDefinition("""
            UPDATE message_outbox
            SET status = 'SENT', sent_at = now(), last_error = NULL,
                provider_message_id = COALESCE(@providerMessageId, provider_message_id),
                delivery_status = COALESCE(delivery_status, 'sent'),
                delivery_updated_at = now()
            WHERE id = @id
            """, new { id, providerMessageId }, cancellationToken: ct));
    }

    public async Task<bool> UpdateDeliveryStatusAsync(string providerMessageId,
        string deliveryStatus, string? error, CancellationToken ct = default)
    {
        await using var connection = await _factory.OpenAsync(ct);
        return await connection.ExecuteAsync(new CommandDefinition("""
            UPDATE message_outbox SET
                delivery_status = @deliveryStatus,
                delivery_updated_at = now(),
                status = CASE WHEN @deliveryStatus = 'failed' THEN 'FAILED' ELSE status END,
                last_error = CASE WHEN @deliveryStatus = 'failed'
                                  THEN COALESCE(@error, last_error) ELSE last_error END
            WHERE provider_message_id = @providerMessageId
            """, new { providerMessageId, deliveryStatus, error = Truncate(error) },
            cancellationToken: ct)) > 0;
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

    public async Task<IReadOnlyList<OutboxMessage>> ListAsync(string? status, string? channel,
        int limit, CancellationToken ct = default)
    {
        await using var connection = await _factory.OpenAsync(ct);
        var rows = await connection.QueryAsync<OutboxMessage>(new CommandDefinition($"""
            SELECT {Columns} FROM message_outbox
            WHERE (@status::varchar IS NULL OR status = @status)
              AND (@channel::varchar IS NULL OR channel = @channel)
            ORDER BY created_at DESC, id DESC
            LIMIT @limit
            """, new { status, channel, limit = Math.Clamp(limit, 1, 200) },
            cancellationToken: ct));
        return rows.ToList();
    }

    public async Task<bool> RetryAsync(Guid id, CancellationToken ct = default)
    {
        await using var connection = await _factory.OpenAsync(ct);
        return await connection.ExecuteAsync(new CommandDefinition("""
            UPDATE message_outbox
            SET status = 'PENDING', attempts = 0, next_attempt_at = now(), last_error = NULL
            WHERE id = @id AND status IN ('FAILED', 'CANCELLED')
            """, new { id }, cancellationToken: ct)) > 0;
    }

    public async Task<bool> CancelPendingAsync(Guid id, CancellationToken ct = default)
    {
        await using var connection = await _factory.OpenAsync(ct);
        return await connection.ExecuteAsync(new CommandDefinition("""
            UPDATE message_outbox SET status = 'CANCELLED'
            WHERE id = @id AND status = 'PENDING'
            """, new { id }, cancellationToken: ct)) > 0;
    }

    private static string Truncate(string? error) =>
        error is null ? "" : error.Length > 1000 ? error[..1000] : error;
}
