using System.Security.Cryptography;
using System.Text;
using Dapper;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Npgsql;
using Ubp.Application.Services;
using Ubp.Core.Models;
using Ubp.Infrastructure.Repositories;
using Ubp.Web.Endpoints;
using Xunit;

namespace Ubp.Tests;

/// <summary>
/// Webhooks واتساب: تحقق Verify Token عند الاشتراك، والتحقق من توقيع X-Hub-Signature-256،
/// وتحديث حالة التسليم وحفظ الرسالة الواردة لكل فرع.
/// </summary>
public sealed class WhatsAppWebhookTests : IClassFixture<TestDb>
{
    private readonly TestDb _db;

    public WhatsAppWebhookTests(TestDb db) => _db = db;

    private async Task<(Guid Branch, WhatsAppWebhookService Webhooks)> SetupAsync(string suffix)
    {
        var actor = await _db.AdminActorAsync();
        var branch = await _db.CreateBranchAsync("WH-B" + suffix, "فرع الويب هوك " + suffix);
        var settings = new BranchWhatsAppSettingsRepository(_db.Factory);
        await settings.UpsertAsync(new BranchWhatsAppSettingsInput
        {
            BranchId = branch, IsEnabled = true, PhoneNumberId = "111111111111111",
            AccessToken = "dummy-token", WebhookVerifyToken = "verify-" + suffix,
            AppSecret = "secret-" + suffix
        }, actor.UserId);
        return (branch, new WhatsAppWebhookService(settings,
            new MessageOutboxRepository(_db.Factory), new WhatsAppInboundRepository(_db.Factory)));
    }

    private static DefaultHttpContext Context(string? query = null, string? body = null,
        string? signature = null)
    {
        var context = new DefaultHttpContext
        {
            RequestServices = new ServiceCollection().AddLogging().BuildServiceProvider()
        };
        context.Response.Body = new MemoryStream();
        if (query is not null) context.Request.QueryString = new QueryString(query);
        if (body is not null)
        {
            var bytes = Encoding.UTF8.GetBytes(body);
            context.Request.Body = new MemoryStream(bytes);
            context.Request.ContentLength = bytes.Length;
            context.Request.ContentType = "application/json";
        }
        if (signature is not null) context.Request.Headers["X-Hub-Signature-256"] = signature;
        return context;
    }

    private static string Signature(string secret, string body) =>
        "sha256=" + Convert.ToHexString(new HMACSHA256(Encoding.UTF8.GetBytes(secret))
            .ComputeHash(Encoding.UTF8.GetBytes(body))).ToLowerInvariant();

    private static async Task<(int Status, string Body)> ExecuteAsync(IResult result,
        HttpContext context)
    {
        await result.ExecuteAsync(context);
        context.Response.Body.Position = 0;
        return (context.Response.StatusCode,
            await new StreamReader(context.Response.Body).ReadToEndAsync());
    }

    [Fact]
    public async Task Verify_returns_the_challenge_for_the_branch_token()
    {
        var f = await SetupAsync("1");

        var ok = await ExecuteAsync(await WhatsAppWebhookEndpoints.VerifyAsync(f.Branch,
            Context("?hub.mode=subscribe&hub.verify_token=verify-1&hub.challenge=abc123"),
            f.Webhooks, default), Context());
        Assert.Equal(200, ok.Status);
        Assert.Equal("abc123", ok.Body);

        var forbidden = await ExecuteAsync(await WhatsAppWebhookEndpoints.VerifyAsync(f.Branch,
            Context("?hub.mode=subscribe&hub.verify_token=wrong&hub.challenge=abc123"),
            f.Webhooks, default), Context());
        Assert.Equal(403, forbidden.Status);
    }

    [Fact]
    public async Task Receive_verifies_signature_and_updates_status_and_inbox()
    {
        var f = await SetupAsync("2");
        var outboxRepo = new MessageOutboxRepository(_db.Factory);
        var inbound = new WhatsAppInboundRepository(_db.Factory);
        var id = await outboxRepo.EnqueueAsync(new OutboxMessageInput
        { Channel = MessageChannels.WhatsApp, Recipient = "+201000000001", Body = "hi" });
        await outboxRepo.MarkSentAsync(id!.Value, "wamid.1");

        const string body = """
            {"object":"whatsapp_business_account","entry":[{"id":"1","changes":[{"field":"messages","value":{
              "messaging_product":"whatsapp",
              "statuses":[{"id":"wamid.1","status":"read","timestamp":"1700000000"}],
              "messages":[{"id":"wamid.IN.1","from":"201000000002","timestamp":"1700000001",
                           "type":"text","text":{"body":"مرحبا"}}]
            }}]}]}
            """;

        var ok = await ExecuteAsync(await WhatsAppWebhookEndpoints.ReceiveAsync(f.Branch,
            Context(body: body, signature: Signature("secret-2", body)), f.Webhooks, default), Context());
        Assert.Equal(200, ok.Status);

        await using var connection = new NpgsqlConnection(TestDb.AdminConnection);
        await connection.OpenAsync();
        Assert.Equal("read", await connection.ExecuteScalarAsync<string>(
            "SELECT delivery_status FROM message_outbox WHERE provider_message_id = 'wamid.1'"));
        Assert.Equal(1, await connection.ExecuteScalarAsync<int>(
            "SELECT count(*)::int FROM whatsapp_inbound_messages " +
            "WHERE wa_message_id = 'wamid.IN.1' AND branch_id = @branch",
            new { branch = f.Branch }));

        var forbidden = await ExecuteAsync(await WhatsAppWebhookEndpoints.ReceiveAsync(f.Branch,
            Context(body: body, signature: "sha256=deadbeef"), f.Webhooks, default), Context());
        Assert.Equal(403, forbidden.Status);
    }

    [Fact]
    public async Task Inbox_lists_received_messages()
    {
        var actor = await _db.AdminActorAsync();
        var f = await SetupAsync("3");
        await _db.WhatsAppInboundRepo.InsertAsync(f.Branch, "wamid.IN.3", "201000000003", "text",
            "اختبار الوارد", DateTimeOffset.UtcNow, "{}");

        var rows = await _db.WhatsAppInbox().ListAsync(actor);
        Assert.Contains(rows, r => r.WaMessageId == "wamid.IN.3" && r.TextBody == "اختبار الوارد");
    }
}
