using Microsoft.Extensions.Logging;
using Ubp.Core.Models;

namespace Ubp.Application.Services;

/// <summary>مزوّد إرسال خارجي (SMS/واتساب/…): نقطة الاستبدال الوحيدة عند ربط مزوّد حقيقي.</summary>
public interface IMessageSender
{
    Task SendAsync(OutboxMessage message, CancellationToken ct = default);
}

/// <summary>مزوّد تجريبي يسجل الرسالة دون إرسالها، مع إخفاء جزء من رقم المستلم.</summary>
public sealed class LoggingMessageSender : IMessageSender
{
    private readonly ILogger<LoggingMessageSender> _logger;

    public LoggingMessageSender(ILogger<LoggingMessageSender> logger) => _logger = logger;

    public Task SendAsync(OutboxMessage message, CancellationToken ct = default)
    {
        _logger.LogInformation("OUTBOX {Channel} -> {Recipient} [{Template}]: {Body}",
            message.Channel, Mask(message.Recipient), message.TemplateKey ?? "-", message.Body);
        return Task.CompletedTask;
    }

    private static string Mask(string recipient) =>
        recipient.Length <= 4 ? "***" : "***" + recipient[^4..];
}
