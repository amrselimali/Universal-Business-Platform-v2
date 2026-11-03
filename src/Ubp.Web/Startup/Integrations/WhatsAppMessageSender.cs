using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Ubp.Application.Services;
using Ubp.Core.Models;
using Ubp.Infrastructure.Repositories;
using Ubp.Web.Services;

namespace Ubp.Web.Startup.Integrations;

/// <summary>
/// مزوّد الإرسال: يقرأ رقم الفرع من حمولة الرسالة ويستخدم إعدادات الفرع (Meta Cloud API)،
/// وإلا يسقط على الإعدادات العامة. تعطيل الإعداد لا يُفشل تشغيل التطبيق.
/// </summary>
public sealed class WhatsAppMessageSender : IMessageSender
{
    private readonly IHttpClientFactory _http;
    private readonly IBranchWhatsAppSettingsRepository _branchSettings;
    private readonly WhatsAppOptions _options;
    private readonly ILogger<WhatsAppMessageSender> _logger;

    public WhatsAppMessageSender(IHttpClientFactory http,
        IBranchWhatsAppSettingsRepository branchSettings, IOptions<WhatsAppOptions> options,
        ILogger<WhatsAppMessageSender> logger)
    {
        _http = http; _branchSettings = branchSettings; _options = options.Value;
        _logger = logger;
    }

    public async Task SendAsync(OutboxMessage message, CancellationToken ct = default)
    {
        if (message.Channel != MessageChannels.WhatsApp)
        {
            _logger.LogInformation("OUTBOX {Channel} -> {Recipient}: {Body}",
                message.Channel, Mask(message.Recipient), message.Body);
            return;
        }

        var (apiUrl, phoneNumberId, token) = await ResolveCredentialsAsync(message, ct);
        if (string.IsNullOrWhiteSpace(phoneNumberId) || string.IsNullOrWhiteSpace(token))
        {
            _logger.LogInformation("OUTBOX WHATSAPP (not configured) -> {Recipient}: {Body}",
                Mask(message.Recipient), message.Body);
            return;
        }

        var to = new string(message.Recipient.Where(char.IsDigit).ToArray());
        if (to.Length < 8) throw new InvalidOperationException("WHATSAPP_INVALID_RECIPIENT");

        var client = _http.CreateClient("whatsapp");
        using var request = new HttpRequestMessage(HttpMethod.Post,
            $"{apiUrl.TrimEnd('/')}/{phoneNumberId}/messages");
        request.Headers.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        request.Content = JsonContent.Create(new
        {
            messaging_product = "whatsapp", to, type = "text", text = new { body = message.Body }
        });

        using var response = await client.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException(
                $"WHATSAPP_SEND_FAILED {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync(ct)}");
    }

    private async Task<(string ApiUrl, string? PhoneNumberId, string? Token)>
        ResolveCredentialsAsync(OutboxMessage message, CancellationToken ct)
    {
        Guid? branchId = null;
        try
        {
            if (!string.IsNullOrWhiteSpace(message.PayloadJson))
            {
                using var doc = JsonDocument.Parse(message.PayloadJson);
                if (doc.RootElement.TryGetProperty("branch_id", out var value) &&
                    Guid.TryParse(value.GetString(), out var parsed))
                    branchId = parsed;
            }
        }
        catch (JsonException) { }

        if (branchId is { } id)
        {
            var branch = await _branchSettings.GetAsync(id, ct);
            if (branch is { IsEnabled: true } &&
                !string.IsNullOrWhiteSpace(branch.PhoneNumberId) &&
                !string.IsNullOrWhiteSpace(branch.AccessToken))
                return (string.IsNullOrWhiteSpace(branch.ApiUrl)
                    ? "https://graph.facebook.com/v20.0" : branch.ApiUrl,
                    branch.PhoneNumberId, branch.AccessToken);
        }

        return (_options.ApiUrl, _options.PhoneNumberId, _options.Token);
    }

    private static string Mask(string recipient) =>
        recipient.Length <= 4 ? "***" : "***" + recipient[^4..];
}
