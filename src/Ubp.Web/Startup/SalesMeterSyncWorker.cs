using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Ubp.Infrastructure.Repositories;

namespace Ubp.Web.Startup;

public sealed class AssetMeterSyncOptions
{
    public bool Enabled { get; set; } = true;
    public int IntervalHours { get; set; } = 24;
}

/// <summary>
/// عامل خلفي يحوّل أسطر فواتير البيع المرحّلة لصناف مرتبطة بعدّاد أصل إلى قراءات عدّاد (SALES).
/// يعمل بلا مستخدم (created_by = null) وبأمان تكرار على مستوى القاعدة (source_line_id).
/// </summary>
public sealed class SalesMeterSyncWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopes;
    private readonly IOptions<AssetMeterSyncOptions> _options;
    private readonly ILogger<SalesMeterSyncWorker> _logger;

    public SalesMeterSyncWorker(IServiceScopeFactory scopes, IOptions<AssetMeterSyncOptions> options,
        ILogger<SalesMeterSyncWorker> logger)
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
                await SyncAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Sales meter sync failed; it will retry on the next tick");
            }

            try
            {
                await Task.Delay(TimeSpan.FromHours(Math.Max(_options.Value.IntervalHours, 1)),
                    stoppingToken);
            }
            catch (OperationCanceledException) { break; }
        }
    }

    /// <summary>مزامنة الشهر الحالي؛ عامّة حتى تختبرها الأدوات دون تشغيل الخدمة.</summary>
    public async Task<int> SyncAsync(CancellationToken ct = default)
    {
        await using var scope = _scopes.CreateAsyncScope();
        var repository = scope.ServiceProvider.GetRequiredService<IAssetRepository>();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var monthStart = new DateOnly(today.Year, today.Month, 1);
        var synced = await repository.SyncSalesReadingsAsync(null, monthStart, today, null, ct);
        if (synced > 0)
            _logger.LogInformation("Sales meter sync updated {Count} assets", synced);
        return synced;
    }
}
