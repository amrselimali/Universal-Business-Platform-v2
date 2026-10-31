using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Ubp.Infrastructure.Repositories;

namespace Ubp.Web.Startup;

public sealed class BiOptions
{
    public bool Enabled { get; set; } = true;
    public int IntervalMinutes { get; set; } = 15;
}

/// <summary>
/// تحديث مجدول لعروض مخطط <c>bi</c> باستخدام <c>REFRESH … CONCURRENTLY</c>
/// (كل جملة في معاملة ضمنية مستقلة، فلا يحجب القرّاء أثناء التحديث).
/// </summary>
public sealed class BiRefreshWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopes;
    private readonly IOptions<BiOptions> _options;
    private readonly ILogger<BiRefreshWorker> _logger;

    public BiRefreshWorker(IServiceScopeFactory scopes, IOptions<BiOptions> options,
        ILogger<BiRefreshWorker> logger)
    {
        _scopes = scopes; _options = options; _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Value.Enabled) return;

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RefreshOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "BI refresh failed; the views keep their previous data");
            }

            try
            {
                await Task.Delay(TimeSpan.FromMinutes(Math.Max(_options.Value.IntervalMinutes, 1)),
                    stoppingToken);
            }
            catch (OperationCanceledException) { break; }
        }
    }

    /// <summary>تحديث واحد؛ عامّة حتى تختبرها الاختبارات دون تشغيل الخدمة.</summary>
    public async Task RefreshOnceAsync(CancellationToken ct = default)
    {
        await using var scope = _scopes.CreateAsyncScope();
        var repository = scope.ServiceProvider.GetRequiredService<IBiRepository>();
        await repository.RefreshConcurrentlyAsync(ct);
        _logger.LogInformation("BI materialized views refreshed");
    }
}
