using System.Text.Json;
using Dapper;
using Npgsql;
using Ubp.Core.Models;
using Ubp.Infrastructure.Data;

namespace Ubp.Infrastructure.Repositories;

public interface IWhatsAppInboundRepository
{
    Task<bool> InsertAsync(Guid branchId, string waMessageId, string fromPhone, string messageType,
        string? text, DateTimeOffset? sentAt, string payloadJson, CancellationToken ct = default);
    Task<IReadOnlyList<WhatsAppInboundMessage>> ListAsync(int limit,
        CancellationToken ct = default);
}

/// <summary>الرسائل الواردة من واتساب: تُحفظ مرة واحدة لكل wamid (منع تكرار Webhook).</summary>
public sealed class WhatsAppInboundRepository : IWhatsAppInboundRepository
{
    private readonly IDbConnectionFactory _factory;
    public WhatsAppInboundRepository(IDbConnectionFactory factory) => _factory = factory;

    public async Task<bool> InsertAsync(Guid branchId, string waMessageId, string fromPhone,
        string messageType, string? text, DateTimeOffset? sentAt, string payloadJson,
        CancellationToken ct = default)
    {
        await using var connection = await _factory.OpenAsync(ct);
        try
        {
            return await connection.ExecuteAsync(new CommandDefinition("""
                INSERT INTO whatsapp_inbound_messages
                    (branch_id, wa_message_id, from_phone, message_type, text_body, sent_at, payload)
                VALUES (@branchId, @waMessageId, @fromPhone, @messageType, @text, @sentAt,
                        CAST(@payloadJson AS jsonb))
                ON CONFLICT (wa_message_id) DO NOTHING
                """, new { branchId, waMessageId, fromPhone, messageType, text, sentAt,
                    payloadJson = string.IsNullOrWhiteSpace(payloadJson) ? "{}" : payloadJson },
                cancellationToken: ct)) > 0;
        }
        catch (PostgresException ex) when (ex.SqlState == "22P05")
        {
            throw new InvalidOperationException("WHATSAPP_INBOUND_PAYLOAD_INVALID");
        }
    }

    public async Task<IReadOnlyList<WhatsAppInboundMessage>> ListAsync(int limit,
        CancellationToken ct = default)
    {
        await using var connection = await _factory.OpenAsync(ct);
        var rows = await connection.QueryAsync<WhatsAppInboundMessage>(new CommandDefinition("""
            SELECT m.id AS Id, m.branch_id AS BranchId, b.name_ar AS BranchName,
                   m.wa_message_id AS WaMessageId, m.from_phone AS FromPhone,
                   m.message_type AS MessageType, m.text_body AS TextBody, m.sent_at AS SentAt,
                   m.created_at AS CreatedAt
            FROM whatsapp_inbound_messages m
            JOIN branches b ON b.id = m.branch_id
            ORDER BY m.created_at DESC, m.id DESC
            LIMIT @limit
            """, new { limit = Math.Clamp(limit, 1, 200) }, cancellationToken: ct));
        return rows.ToList();
    }
}
