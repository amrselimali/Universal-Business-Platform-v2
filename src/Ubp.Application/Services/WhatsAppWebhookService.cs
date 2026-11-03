using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Ubp.Infrastructure.Repositories;

namespace Ubp.Application.Services;

/// <summary>
/// منطق Webhooks واتساب لكل فرع: تحقق Verify Token عند الاشتراك، والتحقق من توقيع
/// <c>X-Hub-Signature-256</c> (HMAC-SHA256 بـApp Secret)، ثم تحديث حالات التسليم
/// وحفظ الرسائل الواردة مرة واحدة لكل wamid.
/// </summary>
public sealed class WhatsAppWebhookService
{
    private readonly IBranchWhatsAppSettingsRepository _settings;
    private readonly IMessageOutboxRepository _outbox;
    private readonly IWhatsAppInboundRepository _inbound;

    public WhatsAppWebhookService(IBranchWhatsAppSettingsRepository settings,
        IMessageOutboxRepository outbox, IWhatsAppInboundRepository inbound)
    {
        _settings = settings; _outbox = outbox; _inbound = inbound;
    }

    public async Task<bool> VerifyAsync(Guid branchId, string mode, string token,
        CancellationToken ct = default)
    {
        var branch = await _settings.GetAsync(branchId, ct);
        return branch is not null && !string.IsNullOrWhiteSpace(branch.WebhookVerifyToken) &&
               string.Equals(mode, "subscribe", StringComparison.Ordinal) &&
               FixedTimeEquals(token, branch.WebhookVerifyToken);
    }

    /// <summary>يعيد false عند عدم تكوين الفرع أو عدم صحة التوقيع (يُترجم 403 في نقطة النهاية).</summary>
    public async Task<bool> ReceiveAsync(Guid branchId, string body, string signature,
        CancellationToken ct = default)
    {
        var branch = await _settings.GetAsync(branchId, ct);
        if (branch is null || string.IsNullOrWhiteSpace(branch.AppSecret)) return false;
        if (!VerifySignature(branch.AppSecret, body, signature)) return false;

        try { await ProcessAsync(branchId, body, ct); }
        catch (JsonException) { /* حمولة غير صالحة بعد التحقق: نتجاهلها ونعيد 200 */ }
        return true;
    }

    private async Task ProcessAsync(Guid branchId, string body, CancellationToken ct)
    {
        using var doc = JsonDocument.Parse(body);
        if (!doc.RootElement.TryGetProperty("entry", out var entries) ||
            entries.ValueKind != JsonValueKind.Array) return;

        foreach (var entry in entries.EnumerateArray())
        {
            if (!entry.TryGetProperty("changes", out var changes) ||
                changes.ValueKind != JsonValueKind.Array) continue;
            foreach (var change in changes.EnumerateArray())
            {
                if (!change.TryGetProperty("value", out var value)) continue;

                if (value.TryGetProperty("statuses", out var statuses) &&
                    statuses.ValueKind == JsonValueKind.Array)
                {
                    foreach (var status in statuses.EnumerateArray())
                    {
                        var id = String(status, "id");
                        var state = String(status, "status");
                        if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(state))
                            continue;
                        await _outbox.UpdateDeliveryStatusAsync(id, state, ExtractError(status), ct);
                    }
                }

                if (value.TryGetProperty("messages", out var messages) &&
                    messages.ValueKind == JsonValueKind.Array)
                {
                    foreach (var message in messages.EnumerateArray())
                    {
                        var id = String(message, "id");
                        var from = String(message, "from");
                        if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(from))
                            continue;
                        var type = String(message, "type") ?? "text";
                        var text = message.TryGetProperty("text", out var t)
                            ? String(t, "body") : null;
                        DateTimeOffset? sentAt = null;
                        if (long.TryParse(String(message, "timestamp"), out var unix))
                            sentAt = DateTimeOffset.FromUnixTimeSeconds(unix);
                        await _inbound.InsertAsync(branchId, id, from, type, text, sentAt,
                            message.GetRawText(), ct);
                    }
                }
            }
        }
    }

    private static string? ExtractError(JsonElement status)
    {
        if (!status.TryGetProperty("errors", out var errors) ||
            errors.ValueKind != JsonValueKind.Array || errors.GetArrayLength() == 0) return null;
        var first = errors[0];
        var message = String(first, "message") ?? String(first, "title");
        var code = String(first, "code");
        return code is null ? message : $"{code}: {message}";
    }

    private static string? String(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() : null;

    private static bool VerifySignature(string appSecret, string body, string header)
    {
        const string prefix = "sha256=";
        if (string.IsNullOrWhiteSpace(header) ||
            !header.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return false;
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(appSecret));
        var computed = Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes(body)))
            .ToLowerInvariant();
        return FixedTimeEquals(computed, header[prefix.Length..].Trim().ToLowerInvariant());
    }

    private static bool FixedTimeEquals(string a, string b) =>
        CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(a),
            Encoding.UTF8.GetBytes(b));
}
