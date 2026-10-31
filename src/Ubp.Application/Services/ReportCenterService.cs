using System.Text.Json;
using Ubp.Core.Models;
using Ubp.Core.Paging;
using Ubp.Core.Results;
using Ubp.Core.Security;
using Ubp.Infrastructure.Repositories;

namespace Ubp.Application.Services;

/// <summary>
/// مركز التقارير: يعرض التقارير المعتمدة حسب صلاحيات المستخدم، ويحفظ/يحذف فلاتره،
/// ويصدّر البيانات إلى CSV/XLSX. كل عملية تمر بصلاحية صريحة على شاشة المركز.
/// </summary>
public sealed class ReportCenterService
{
    /// <summary>حد أقصى لصفوف التصدير (40 صفحة × 25) لمنع تقارير ضخمة بلا حدود.</summary>
    public const int MaxExportRows = 1000;
    private const int PageSize = PageRequest.MaxPageSize;

    private readonly ISavedReportFilterRepository _filters;
    private readonly PermissionService _permissions;
    private readonly StockReportService _stockReports;

    public ReportCenterService(ISavedReportFilterRepository filters, PermissionService permissions,
        StockReportService stockReports)
    {
        _filters = filters; _permissions = permissions; _stockReports = stockReports;
    }

    public async Task<IReadOnlyList<ReportDefinition>> ReportsAsync(ActorContext actor,
        CancellationToken ct = default)
    {
        await _permissions.RequireAsync(actor, ScreenCodes.ReportsCenter, ActionCodes.View, ct: ct);
        var visible = await _permissions.GetVisibleScreenCodesAsync(actor, ct);
        return ReportCatalog.All
            .Where(r => visible.Contains(r.ScreenCode, StringComparer.OrdinalIgnoreCase))
            .ToList();
    }

    public async Task<IReadOnlyList<SavedReportFilter>> FiltersAsync(ActorContext actor,
        string reportKey, CancellationToken ct = default)
    {
        await _permissions.RequireAsync(actor, ScreenCodes.ReportsCenter, ActionCodes.View, ct: ct);
        EnsureReport(reportKey);
        return await _filters.ListAsync(actor.UserId, reportKey, ct);
    }

    public async Task<SavedReportFilter> SaveFilterAsync(ActorContext actor, string reportKey,
        string filterName, IReadOnlyDictionary<string, string?> values, CancellationToken ct = default)
    {
        await _permissions.RequireAsync(actor, ScreenCodes.ReportsCenter, ActionCodes.SaveFilter, ct: ct);
        EnsureReport(reportKey);
        var name = filterName?.Trim() ?? "";
        if (name.Length is < 1 or > 120)
            throw new ValidationAppException("validation.filter_name_required");

        var cleaned = values
            .Where(p => !string.IsNullOrWhiteSpace(p.Key) && !string.IsNullOrWhiteSpace(p.Value))
            .ToDictionary(p => p.Key.Trim(), p => p.Value!.Trim(), StringComparer.OrdinalIgnoreCase);
        var json = JsonSerializer.Serialize(cleaned);
        return await _filters.SaveAsync(actor.UserId, reportKey, name, json, ct);
    }

    public async Task DeleteFilterAsync(ActorContext actor, Guid id, CancellationToken ct = default)
    {
        await _permissions.RequireAsync(actor, ScreenCodes.ReportsCenter, ActionCodes.DeleteFilter,
            ct: ct);
        if (!await _filters.DeleteAsync(id, actor.UserId, ct))
            throw new NotFoundAppException();
    }

    /// <summary>تصدير أرصدة المخزون بتطبيق فلتر (مخزن/تضمين الأصفار) إلى CSV أو XLSX.</summary>
    public async Task<ReportExport> ExportStockBalancesAsync(ActorContext actor,
        ReportExportFormat format, Guid? warehouseId, bool includeZero, CancellationToken ct = default)
    {
        await _permissions.RequireAsync(actor, ScreenCodes.ReportsCenter, ActionCodes.View, ct: ct);
        await _permissions.RequireAsync(actor, ScreenCodes.ReportsCenter,
            format == ReportExportFormat.Csv ? ActionCodes.ExportCsv : ActionCodes.ExportXlsx, ct: ct);

        var rows = new List<StockBalance>();
        for (var page = 1; rows.Count < MaxExportRows; page++)
        {
            var result = await _stockReports.BalancesAsync(actor,
                new PageRequest { Page = page, PageSize = PageSize }, warehouseId, includeZero, ct);
            rows.AddRange(result.Items);
            if (page * PageSize >= result.Total || result.Items.Count == 0) break;
        }

        var headers = new[] { "item_code", "item_name", "warehouse", "on_hand", "average_cost",
            "inventory_value" };
        var data = rows.Take(MaxExportRows)
            .Select(b => (IReadOnlyList<string?>)
            [
                b.ItemCode, b.ItemName, b.WarehouseName,
                b.OnHand.ToString(System.Globalization.CultureInfo.InvariantCulture),
                b.AverageCost.ToString(System.Globalization.CultureInfo.InvariantCulture),
                b.InventoryValue.ToString(System.Globalization.CultureInfo.InvariantCulture)
            ])
            .ToList();

        var stamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmm");
        return format == ReportExportFormat.Csv
            ? new ReportExport($"stock-balances-{stamp}.csv", "text/csv; charset=utf-8",
                ReportExporter.Csv(headers, data))
            : new ReportExport($"stock-balances-{stamp}.xlsx",
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                ReportExporter.Xlsx("Stock balances", headers, data));
    }

    private static void EnsureReport(string reportKey)
    {
        if (ReportCatalog.Find(reportKey) is null)
            throw new ValidationAppException("validation.report_key_unknown");
    }
}
