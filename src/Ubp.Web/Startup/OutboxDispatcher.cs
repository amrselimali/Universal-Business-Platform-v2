using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Ubp.Application.Services;
using Ubp.Core.Models;
using Ubp.Infrastructure.Repositories;

namespace Ubp.Web.Startup;

public sealed class OutboxOptions
{
    public bool Enabled { get; set; } = true;
    public int PollSeconds { get; set; } = 10;
    public int BatchSize { get; set; } = 20;
    public short MaxAttempts { get; set; } = 5;
}

/// <summary>
/// معالج خلفي لصندوق الرسائل: يستأجر دفعة، يرسلها عبر <see cref="IMessageSender"/>،
/// ويسجل الفشل بموعد محاولة تالٍ (backoff أسّي بسقف ساعة) حتى بلوغ الحد الأقصى.
/// </summary>
public sealed class OutboxDispatcher : BackgroundService
{
    private static readonly TimeSpan Lease = TimeSpan.FromMinutes(2);
    private readonly IServiceScopeFactory _scopes;
    private readonly IOptions<OutboxOptions> _options;
    private readonly ILogger<OutboxDispatcher> _logger;

    public OutboxDispatcher(IServiceScopeFactory scopes, IOptions<OutboxOptions> options,
        ILogger<OutboxDispatcher> logger)
    {
        _scopes = scopes; _options = options; _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (_options.Value.Enabled) await ProcessBatchAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Outbox batch failed");
            }

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(Math.Max(_options.Value.PollSeconds, 1)),
                    stoppingToken);
            }
            catch (OperationCanceledException) { break; }
        }
    }

    /// <summary>معالجة دفعة واحدة؛ عامّة حتى تختبرها الاختبارات دون تشغيل الخدمة.</summary>
    public async Task<int> ProcessBatchAsync(CancellationToken ct = default)
    {
        var options = _options.Value;
        await using var scope = _scopes.CreateAsyncScope();
        var repository = scope.ServiceProvider.GetRequiredService<IMessageOutboxRepository>();
        var sender = scope.ServiceProvider.GetRequiredService<IMessageSender>();

        var due = await repository.ClaimDueAsync(options.BatchSize, Lease, ct);
        foreach (var message in due)
        {
            try
            {
                await sender.SendAsync(message, ct);
                await repository.MarkSentAsync(message.Id, ct);
            }
            catch (Exception ex)
            {
                var attempts = (short)(message.Attempts + 1);
                await repository.MarkFailedAsync(message.Id, attempts,
                    DateTimeOffset.UtcNow.Add(Backoff(attempts)), ex.Message,
                    options.MaxAttempts, ct);
                _logger.LogWarning(ex, "Outbox message {Id} failed (attempt {Attempts})",
                    message.Id, attempts);
            }
        }
        return due.Count;
    }

    /// <summary>تأخير أسّي: 1→60ث، 2→120ث … بسقف ساعة.</summary>
    public static TimeSpan Backoff(short attempts)
    {
        var seconds = Math.Min(Math.Pow(2, Math.Max(attempts, (short)1)) * 30, 3600);
        return TimeSpan.FromSeconds(seconds);
    }
}
