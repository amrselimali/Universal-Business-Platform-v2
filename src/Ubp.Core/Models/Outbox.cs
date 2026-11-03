namespace Ubp.Core.Models;

public static class MessageChannels
{
    public const string Sms = "SMS";
    public const string WhatsApp = "WHATSAPP";
    public const string Email = "EMAIL";
    public const string Push = "PUSH";

    public static string Normalize(string? value) =>
        value is not null && new[] { Sms, WhatsApp, Email, Push }.Contains(value.ToUpperInvariant())
            ? value.ToUpperInvariant() : Sms;
}

public static class OutboxStatuses
{
    public const string Pending = "PENDING";
    public const string Sent = "SENT";
    public const string Failed = "FAILED";
    public const string Cancelled = "CANCELLED";
}

/// <summary>رسالة خارجية في صندوق الإرسال (الترحيل 0042).</summary>
public sealed class OutboxMessage
{
    public Guid Id { get; set; }
    public string Channel { get; set; } = MessageChannels.Sms;
    public string Recipient { get; set; } = "";
    public string? TemplateKey { get; set; }
    public string Body { get; set; } = "";
    public string PayloadJson { get; set; } = "{}";
    public string? DedupeKey { get; set; }
    public string Status { get; set; } = OutboxStatuses.Pending;
    public short Attempts { get; set; }
    public DateTimeOffset NextAttemptAt { get; set; }
    public string? LastError { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? SentAt { get; set; }
    /// <summary>معرّف الرسالة لدى Meta (wamid) لربط تحديثات الحالة.</summary>
    public string? ProviderMessageId { get; set; }
    /// <summary>حالة التسليم من Webhook: sent/delivered/read/failed.</summary>
    public string? DeliveryStatus { get; set; }
    public DateTimeOffset? DeliveryUpdatedAt { get; set; }
}

/// <summary>رسالة واردة من واتساب عبر Webhook.</summary>
public sealed class WhatsAppInboundMessage
{
    public Guid Id { get; set; }
    public Guid BranchId { get; set; }
    public string? BranchName { get; set; }
    public string WaMessageId { get; set; } = "";
    public string FromPhone { get; set; } = "";
    public string MessageType { get; set; } = "text";
    public string? TextBody { get; set; }
    public DateTimeOffset? SentAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class OutboxMessageInput
{
    public string Channel { get; set; } = MessageChannels.Sms;
    public string Recipient { get; set; } = "";
    public string? TemplateKey { get; set; }
    public string Body { get; set; } = "";
    public string? DedupeKey { get; set; }
    /// <summary>حمولة JSON إضافية (مثل branch_id) تُستخدم عند الإرسال.</summary>
    public string PayloadJson { get; set; } = "{}";
}
