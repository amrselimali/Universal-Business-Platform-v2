using Dapper;
using Npgsql;
using Ubp.Application.Services;
using Ubp.Core.Models;
using Ubp.Core.Results;
using Ubp.Infrastructure.Repositories;
using Xunit;

namespace Ubp.Tests;

/// <summary>
/// صندوق الرسائل (الترحيل 0042): منع التكرار بمفتاح dedupe، والاستئجار مرة واحدة لكل رسالة.
/// </summary>
public sealed class OutboxTests : IClassFixture<TestDb>
{
    private readonly TestDb _db;

    public OutboxTests(TestDb db) => _db = db;

    private MessageOutboxRepository Repo() => new(_db.Factory);

    private static OutboxMessageInput NewInput(string? dedupeKey = null, string body = "مرحباً") =>
        new()
        {
            Channel = MessageChannels.WhatsApp,
            Recipient = "+201000000001",
            TemplateKey = "booking.confirmation",
            Body = body,
            DedupeKey = dedupeKey
        };

    [Fact]
    public async Task Enqueue_with_the_same_dedupe_key_inserts_once()
    {
        var first = await Repo().EnqueueAsync(NewInput("dedupe-1"));
        var second = await Repo().EnqueueAsync(NewInput("dedupe-1"));

        Assert.NotNull(first);
        Assert.Null(second);

        await using var connection = new NpgsqlConnection(TestDb.AdminConnection);
        await connection.OpenAsync();
        var rows = await connection.ExecuteScalarAsync<int>(
            "SELECT count(*)::int FROM message_outbox WHERE dedupe_key = 'dedupe-1'");
        Assert.Equal(1, rows);
    }

    [Fact]
    public async Task Service_rejects_empty_recipient_or_body()
    {
        var service = new OutboxService(Repo());

        var recipient = await Assert.ThrowsAsync<ValidationAppException>(async () =>
            await service.EnqueueAsync(new OutboxMessageInput { Recipient = "  ", Body = "x" }));
        Assert.Equal("validation.outbox_recipient_required", recipient.Code);

        var body = await Assert.ThrowsAsync<ValidationAppException>(async () =>
            await service.EnqueueAsync(new OutboxMessageInput { Recipient = "+20100", Body = " " }));
        Assert.Equal("validation.outbox_body_required", body.Code);
    }

    [Fact]
    public async Task Claim_leases_each_due_message_once()
    {
        var repo = Repo();
        var ids = new List<Guid>();
        for (var i = 0; i < 3; i++)
            ids.Add((await repo.EnqueueAsync(NewInput("claim-" + i)))!.Value);

        var first = await repo.ClaimDueAsync(100, TimeSpan.FromMinutes(2));
        Assert.All(ids, id => Assert.Contains(first, m => m.Id == id));

        var second = await repo.ClaimDueAsync(100, TimeSpan.FromMinutes(2));
        Assert.Empty(second);
    }

    [Fact]
    public async Task Claimed_messages_return_only_after_the_lease_expires()
    {
        var repo = Repo();
        var id = (await repo.EnqueueAsync(NewInput("lease-1")))!.Value;

        // مهلة قصيرة جداً: الرسالة مستأجرة الآن، وتعود مستحقة بعد انتهائها.
        var leased = await repo.ClaimDueAsync(100, TimeSpan.FromMilliseconds(150));
        Assert.Contains(leased, m => m.Id == id);

        await Task.Delay(300);
        var reclaimed = await repo.ClaimDueAsync(100, TimeSpan.FromMinutes(2));
        Assert.Contains(reclaimed, m => m.Id == id);
    }

    [Fact]
    public async Task Failed_message_stores_the_attempt_and_the_next_retry_time()
    {
        var repo = Repo();
        var id = (await repo.EnqueueAsync(NewInput("fail-1")))!.Value;
        var next = DateTimeOffset.UtcNow.AddMinutes(3);

        await repo.MarkFailedAsync(id, 2, next, "provider down", maxAttempts: 5);

        var message = await repo.GetAsync(id);
        Assert.NotNull(message);
        Assert.Equal(OutboxStatuses.Pending, message!.Status);
        Assert.Equal(2, message.Attempts);
        Assert.Equal("provider down", message.LastError);
        Assert.True(message.NextAttemptAt >= next.AddSeconds(-1));
    }
}
