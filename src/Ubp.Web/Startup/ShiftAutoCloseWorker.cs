using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Ubp.Application.Services;

namespace Ubp.Web.Startup;

/// <summary>
/// إغلاق آلي للورديات الساعة 5 صباحاً كل يوم: يغلق كل وردية مفتوحة تجاوزت الخامسة
/// بمطابقة المتوقع، ويحوّل تسجيلاتها للمراجعة (0074).
/// </summary>
public sealed class ShiftAutoCloseWorker : BackgroundService
{
    private static readonly TimeSpan Poll = TimeSpan.FromMinutes(5);
    private readonly IServiceScopeFactory _scopes;
    private readonly ILogger<ShiftAutoCloseWorker> _logger;

    public ShiftAutoCloseWorker(IServiceScopeFactory scopes, ILogger<ShiftAutoCloseWorker> logger)
    {
        _scopes = scopes; _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var closed = await ProcessOnceAsync(stoppingToken);
                if (closed > 0)
                    _logger.LogInformation("Auto-closed {Count} cashier shifts at 05:00", closed);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Shift auto-close failed");
            }

            try { await Task.Delay(Poll, stoppingToken); }
            catch (OperationCanceledException) { break; }
        }
    }

    /// <summary>دورة واحدة؛ عامّة حتى تختبرها الاختبارات دون تشغيل الخدمة.</summary>
    public async Task<int> ProcessOnceAsync(CancellationToken ct = default)
    {
        await using var scope = _scopes.CreateAsyncScope();
        var formatter = scope.ServiceProvider.GetRequiredService<IDisplayFormatter>();
        var now = TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, formatter.Zone);
        var treasury = scope.ServiceProvider.GetRequiredService<TreasuryService>();
        return await treasury.AutoCloseAsync(Cutoff(now), ct);
    }

    /// <summary>آخر الخامسة صباحاً مرّت: وردية فُتحت بعدها لا تُغلق قبل الخامسة التالية.</summary>
    public static DateTimeOffset Cutoff(DateTimeOffset now)
    {
        var cutoff = new DateTimeOffset(now.Year, now.Month, now.Day, 5, 0, 0, now.Offset);
        return now < cutoff ? cutoff.AddDays(-1) : cutoff;
    }
}
