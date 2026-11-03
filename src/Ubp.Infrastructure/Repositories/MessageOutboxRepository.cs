using Dapper;
using Npgsql;
using Ubp.Core.Models;
using Ubp.Core.Results;
using Ubp.Infrastructure.Data;

namespace Ubp.Infrastructure.Repositories;

public interface IMessageOutboxRepository
{
    /// <summary>يضيف رسالة؛ يرجع null إذا كان dedupe_key موجوداً مسبقاً.</summary>
    Task<Guid?> EnqueueAsync(OutboxMessageInput input, CancellationToken ct = default);
    /// <summary>يستأجر رسائل مستحقة (lease) ويعيدها للإرسال.</summary>
    Task<IReadOnlyList<OutboxMessage>> ClaimDueAsync(int batchSize, TimeSpan lease,
        CancellationToken ct = default);
    Task MarkSentAsync(Guid id, CancellationToken ct = default);
    /// <summary>تسجيل معرّف Meta للرسالة عند الإرسال الناجح.</summary>
    Task MarkSentAsync(Guid id, string? providerMessageId, CancellationToken ct = default);
    /// <summary>تحديث حالة التسليم من Webhook (sent/delivered/read/failed).</summary>
    Task<bool> UpdateDeliveryStatusAsync(string providerMessageId, string deliveryStatus,
        string? error, CancellationToken ct = default);
    /// <summary>يسجل فشلاً: attempts + 1، وموعد المحاولة التالية أو FAILED عند بلوغ السقف.</summary>
    Task MarkFailedAsync(Guid id, short attempts, DateTimeOffset nextAttemptAt, string error,
        short maxAttempts, CancellationToken ct = default);
    Task<int> CountByStatusAsync(string status, CancellationToken ct = default);
    Task<OutboxMessage?> GetAsync(Guid id, CancellationToken ct = default);
    /// <summary>قائمة الرسائل لأحدث الرسائل مع فلترة اختيارية بالحالة/القناة.</summary>
    Task<IReadOnlyList<OutboxMessage>> ListAsync(string? status, string? channel, int limit,
        CancellationToken ct = default);
    /// <summary>إعادة محاولة رسالة فاشلة/ملغاة (PENDING, attempts=0, الآن).</summary>
    Task<bool> RetryAsync(Guid id, CancellationToken ct = default);
    /// <summary>إلغاء رسالة ما زالت قيد الانتظار.</summary>
    Task<bool> CancelPendingAsync(Guid id, CancellationToken ct = default);
}

/// <summary>
/// صندوق الإرسال: الاستئجار بـ <c>FOR UPDATE SKIP LOCKED</c> مع إزاحة <c>next_attempt_at</c>
/// كمهلة (lease) حتى لا يعالج معالجان نفس الرسالة.
/// </summary>
public sealed partial class MessageOutboxRepository : IMessageOutboxRepository
{
    internal const string Columns = """
        id AS Id, channel AS Channel, recipient AS Recipient, template_key AS TemplateKey,
        body AS Body, payload::text AS PayloadJson, dedupe_key AS DedupeKey, status AS Status,
        attempts AS Attempts, next_attempt_at AS NextAttemptAt, last_error AS LastError,
        created_at AS CreatedAt, sent_at AS SentAt, provider_message_id AS ProviderMessageId,
        delivery_status AS DeliveryStatus, delivery_updated_at AS DeliveryUpdatedAt
        """;

    internal const string ColumnsWithAlias = """
        m.id AS Id, m.channel AS Channel, m.recipient AS Recipient, m.template_key AS TemplateKey,
        m.body AS Body, m.payload::text AS PayloadJson, m.dedupe_key AS DedupeKey, m.status AS Status,
        m.attempts AS Attempts, m.next_attempt_at AS NextAttemptAt, m.last_error AS LastError,
        m.created_at AS CreatedAt, m.sent_at AS SentAt,
        m.provider_message_id AS ProviderMessageId, m.delivery_status AS DeliveryStatus,
        m.delivery_updated_at AS DeliveryUpdatedAt
        """;

    private readonly IDbConnectionFactory _factory;

    public MessageOutboxRepository(IDbConnectionFactory factory) => _factory = factory;

    public async Task<Guid?> EnqueueAsync(OutboxMessageInput input, CancellationToken ct = default)
    {
        const string sql = """
            INSERT INTO message_outbox (channel, recipient, template_key, body, dedupe_key, payload)
            VALUES (@Channel, @Recipient, @TemplateKey, @Body, @DedupeKey,
                    CAST(@PayloadJson AS jsonb))
            ON CONFLICT (dedupe_key) WHERE dedupe_key IS NOT NULL DO NOTHING
            RETURNING id
            """;
        await using var connection = await _factory.OpenAsync(ct);
        try
        {
            return await connection.ExecuteScalarAsync<Guid?>(new CommandDefinition(sql, new
            {
                Channel = MessageChannels.Normalize(input.Channel),
                Recipient = input.Recipient.Trim(),
                input.TemplateKey,
                Body = input.Body.Trim(),
                DedupeKey = Normalize(input.DedupeKey),
                PayloadJson = string.IsNullOrWhiteSpace(input.PayloadJson) ? "{}" : input.PayloadJson
            }, cancellationToken: ct));
        }
        catch (PostgresException ex) when (ex.SqlState == "23514")
        {
            throw new ValidationAppException("validation.outbox_message_invalid");
        }
    }

    public async Task<IReadOnlyList<OutboxMessage>> ClaimDueAsync(int batchSize, TimeSpan lease,
        CancellationToken ct = default)
    {
        var sql = $"""
            WITH due AS (
                SELECT id FROM message_outbox
                WHERE status = 'PENDING' AND next_attempt_at <= now()
                ORDER BY next_attempt_at
                FOR UPDATE SKIP LOCKED
                LIMIT @batchSize
            )
            UPDATE message_outbox m SET next_attempt_at = now() + @lease
            FROM due WHERE m.id = due.id
            RETURNING {ColumnsWithAlias}
            """;
        await using var connection = await _factory.OpenAsync(ct);
        var rows = await connection.QueryAsync<OutboxMessage>(new CommandDefinition(sql, new
        {
            batchSize = Math.Clamp(batchSize, 1, 100), lease
        }, cancellationToken: ct));
        return rows.ToList();
    }

    private static string? Normalize(string? key) =>
        string.IsNullOrWhiteSpace(key) ? null : key.Trim();
}
