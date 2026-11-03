using System.Text;
using Ubp.Application.Services;

namespace Ubp.Web.Endpoints;

/// <summary>
/// Webhooks واتساب (Meta Cloud API): نقاط عامة بدون مصادقة، ومنطق التحقق/المعالجة
/// في <see cref="WhatsAppWebhookService"/> (Verify Token لكل فرع + توقيع X-Hub-Signature-256).
/// </summary>
public static class WhatsAppWebhookEndpoints
{
    public static IEndpointRouteBuilder MapWhatsAppWebhookEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/webhooks/whatsapp").AllowAnonymous().DisableAntiforgery();
        group.MapGet("/{branchId:guid}", VerifyAsync);
        group.MapPost("/{branchId:guid}", ReceiveAsync);
        return app;
    }

    /// <summary>تحقق Meta من الـCallback URL: يُعاد التحدي فقط عند مطابقة Verify Token للفرع.</summary>
    public static async Task<IResult> VerifyAsync(Guid branchId, HttpContext context,
        WhatsAppWebhookService webhooks, CancellationToken ct)
    {
        var mode = context.Request.Query["hub.mode"].ToString();
        var token = context.Request.Query["hub.verify_token"].ToString();
        var challenge = context.Request.Query["hub.challenge"].ToString();
        return await webhooks.VerifyAsync(branchId, mode, token, ct)
            ? Results.Text(challenge, "text/plain")
            : Results.StatusCode(StatusCodes.Status403Forbidden);
    }

    /// <summary>استقبال رسائل/حالات واتساب بعد التحقق من التوقيع.</summary>
    public static async Task<IResult> ReceiveAsync(Guid branchId, HttpContext context,
        WhatsAppWebhookService webhooks, CancellationToken ct)
    {
        using var reader = new StreamReader(context.Request.Body, Encoding.UTF8);
        var body = await reader.ReadToEndAsync(ct);
        var signature = context.Request.Headers["X-Hub-Signature-256"].ToString();
        return await webhooks.ReceiveAsync(branchId, body, signature, ct)
            ? Results.Ok()
            : Results.StatusCode(StatusCodes.Status403Forbidden);
    }
}
