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
}

public sealed class OutboxMessageInput
{
    public string Channel { get; set; } = MessageChannels.Sms;
    public string Recipient { get; set; } = "";
    public string? TemplateKey { get; set; }
    public string Body { get; set; } = "";
    public string? DedupeKey { get; set; }
}
