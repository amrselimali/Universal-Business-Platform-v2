using System.Net.Http.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Ubp.Application.Services;
using Ubp.Core.Models;

namespace Ubp.Web.Services;

/// <summary>إعدادات إرسال واتساب (Meta Cloud API أو بوابة متوافقة).</summary>
public sealed class WhatsAppOptions
{
    public bool Enabled { get; set; }
    public string ApiUrl { get; set; } = "https://graph.facebook.com/v20.0";
    public string? PhoneNumberId { get; set; }
    public string? Token { get; set; }
}

/// <summary>
/// مزوّد الإرسال: يرسل رسائل واتساب عبر البوابة عند تفعيلها، ويسجّل غيرها (SMS/…) دون إرسال.
/// تعطيل الإعداد أو غيابه لا يُفشل تشغيل التطبيق.
/// </summary>
public sealed class WhatsAppMessageSender : IMessageSender
{
    private readonly IHttpClientFactory _http;
    private readonly WhatsAppOptions _options;
    private readonly ILogger<WhatsAppMessageSender> _logger;

    public WhatsAppMessageSender(IHttpClientFactory http, IOptions<WhatsAppOptions> options,
        ILogger<WhatsAppMessageSender> logger)
    {
        _http = http; _options = options.Value; _logger = logger;
    }

    public async Task SendAsync(OutboxMessage message, CancellationToken ct = default)
    {
        if (message.Channel != MessageChannels.WhatsApp || !_options.Enabled ||
            string.IsNullOrWhiteSpace(_options.PhoneNumberId) ||
            string.IsNullOrWhiteSpace(_options.Token))
        {
            _logger.LogInformation("OUTBOX {Channel} -> {Recipient}: {Body}",
                message.Channel, Mask(message.Recipient), message.Body);
            return;
        }

        var to = new string(message.Recipient.Where(char.IsDigit).ToArray());
        if (to.Length < 8) throw new InvalidOperationException("WHATSAPP_INVALID_RECIPIENT");

        var client = _http.CreateClient("whatsapp");
        using var request = new HttpRequestMessage(HttpMethod.Post,
            $"{_options.ApiUrl.TrimEnd('/')}/{_options.PhoneNumberId}/messages");
        request.Headers.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _options.Token);
        request.Content = JsonContent.Create(new
        {
            messaging_product = "whatsapp", to, type = "text", text = new { body = message.Body }
        });

        using var response = await client.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException(
                $"WHATSAPP_SEND_FAILED {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync(ct)}");
    }

    private static string Mask(string recipient) =>
        recipient.Length <= 4 ? "***" : "***" + recipient[^4..];
}
