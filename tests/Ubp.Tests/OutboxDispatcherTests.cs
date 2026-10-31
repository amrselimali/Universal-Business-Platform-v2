using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Ubp.Application.Services;
using Ubp.Core.Models;
using Ubp.Infrastructure.Repositories;
using Ubp.Web.Startup;
using Xunit;

namespace Ubp.Tests;

/// <summary>
/// المعالج الخلفي للصندوق: الإرسال الناجح يقفل الرسالة، والفشل يجدول محاولة تالٍ ثم يصبح نهائياً.
/// </summary>
public sealed class OutboxDispatcherTests : IClassFixture<TestDb>
{
    private readonly TestDb _db;

    public OutboxDispatcherTests(TestDb db) => _db = db;

    private sealed class FakeSender : IMessageSender
    {
        public bool ShouldFail { get; set; }
        public List<OutboxMessage> Sent { get; } = new();

        public Task SendAsync(OutboxMessage message, CancellationToken ct = default)
        {
            if (ShouldFail) throw new InvalidOperationException("provider down");
            Sent.Add(message);
            return Task.CompletedTask;
        }
    }

    private MessageOutboxRepository Repo() => new(_db.Factory);

    private OutboxDispatcher Dispatcher(IMessageSender sender, short maxAttempts = 3)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IMessageOutboxRepository>(Repo());
        services.AddSingleton(sender);
        var provider = services.BuildServiceProvider();
        return new OutboxDispatcher(provider.GetRequiredService<IServiceScopeFactory>(),
            Options.Create(new OutboxOptions
            {
                Enabled = true, PollSeconds = 1, BatchSize = 10, MaxAttempts = maxAttempts
            }),
            NullLogger<OutboxDispatcher>.Instance);
    }

    private static OutboxMessageInput NewInput(string dedupeKey, string? recipient = null) => new()
    {
        Channel = MessageChannels.Sms,
        Recipient = recipient ?? "+201000000002",
        Body = "رسالة اختبار",
        DedupeKey = dedupeKey
    };

    [Fact]
    public async Task Successful_send_marks_the_message_as_sent()
    {
        var id = (await Repo().EnqueueAsync(NewInput("dispatch-ok")))!.Value;
        var sender = new FakeSender();

        var processed = await Dispatcher(sender).ProcessBatchAsync();

        Assert.Equal(1, processed);
        Assert.Single(sender.Sent);
        var message = await Repo().GetAsync(id);
        Assert.Equal(OutboxStatuses.Sent, message!.Status);
        Assert.NotNull(message.SentAt);
    }

    [Fact]
    public async Task Failure_reschedules_with_backoff_and_stays_pending()
    {
        var id = (await Repo().EnqueueAsync(NewInput("dispatch-fail")))!.Value;
        var sender = new FakeSender { ShouldFail = true };

        await Dispatcher(sender).ProcessBatchAsync();

        var message = await Repo().GetAsync(id);
        Assert.Equal(OutboxStatuses.Pending, message!.Status);
        Assert.Equal(1, message.Attempts);
        Assert.Equal("provider down", message.LastError);
        Assert.True(message.NextAttemptAt > DateTimeOffset.UtcNow);
    }

    [Fact]
    public async Task Reaching_max_attempts_makes_the_message_terminal()
    {
        var id = (await Repo().EnqueueAsync(NewInput("dispatch-terminal")))!.Value;
        await Repo().MarkFailedAsync(id, 3, DateTimeOffset.UtcNow.AddMinutes(5), "still down",
            maxAttempts: 3);

        var message = await Repo().GetAsync(id);
        Assert.Equal(OutboxStatuses.Failed, message!.Status);
        Assert.Equal(3, message.Attempts);
    }

    [Theory]
    [InlineData((short)1, 60)]
    [InlineData((short)2, 120)]
    [InlineData((short)3, 240)]
    [InlineData((short)10, 3600)]
    public void Backoff_grows_exponentially_and_caps_at_one_hour(short attempts, int seconds)
    {
        Assert.Equal(TimeSpan.FromSeconds(seconds), OutboxDispatcher.Backoff(attempts));
    }
}
