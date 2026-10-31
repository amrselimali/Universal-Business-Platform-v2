using Ubp.Core.Models;
using Ubp.Core.Results;
using Ubp.Core.Security;
using Ubp.Infrastructure.Repositories;

namespace Ubp.Application.Services;

/// <summary>
/// مشغّل التقارير القياسية: صلاحية واحدة (مركز التقارير) + قصّ الفروع في الخدمة،
/// ويعيد جدولاً موحّداً يستخدمه العرض والتصدير معاً (مصدر واحد للشكل والبيانات).
/// </summary>
public sealed class StandardReportService
{
    private readonly IStandardReportRepository _reports;
    private readonly PermissionService _permissions;

    public StandardReportService(IStandardReportRepository reports, PermissionService permissions)
    {
        _reports = reports; _permissions = permissions;
    }

    public async Task<ReportTable> RunAsync(ActorContext actor, string reportKey, ReportFilter filter,
        CancellationToken ct = default)
    {
        await _permissions.RequireAsync(actor, ScreenCodes.ReportsCenter, ActionCodes.View, ct: ct);
        if (filter.To < filter.From) throw new ValidationAppException("validation.date_range");

        var from = DateOnly.FromDateTime(filter.From);
        var to = DateOnly.FromDateTime(filter.To);
        var branches = Scope(actor, filter.BranchIds);

        return reportKey switch
        {
            StandardReportKeys.Vat => await _reports.VatSummaryAsync(from, to, branches, ct),
            StandardReportKeys.ItemMargin => await _reports.ItemMarginAsync(from, to, branches, ct),
            StandardReportKeys.CustomerMeter => await _reports.CustomerMeterUsageAsync(from, to,
                branches, ct),
            StandardReportKeys.Reservations => await _reports.ReservationStatusAsync(from, to,
                branches, ct: ct),
            StandardReportKeys.ShiftVariance => await _reports.ShiftVarianceAsync(from, to, branches, ct),
            StandardReportKeys.DeadStock => await _reports.DeadStockAsync(to, branches, ct: ct),
            StandardReportKeys.SupplierPerformance => await _reports.SupplierPerformanceAsync(from,
                to, branches, ct),
            StandardReportKeys.StockAging => await _reports.StockAgingAsync(to, branches, ct),
            StandardReportKeys.StockMovements => await _reports.StockMovementsAsync(from, to, branches, ct),
            StandardReportKeys.StockFlow => await _reports.StockFlowAsync(from, to, branches, ct),
            StandardReportKeys.StockValueByWarehouse => await _reports.StockValueByWarehouseAsync(
                branches, ct),
            StandardReportKeys.Commissions => await _reports.CommissionsAsync(from, to, ct),
            StandardReportKeys.BookingConversion => await _reports.BookingConversionAsync(from, to,
                branches, ct),
            StandardReportKeys.UnappliedAdvances => await _reports.UnappliedAdvancesAsync(branches, ct),
            StandardReportKeys.CreditLimits => await _reports.CreditLimitsAsync(branches, ct),
            StandardReportKeys.FollowUps => await _reports.FollowUpsAsync(from, to, ct),
            StandardReportKeys.DepreciationSchedule => await _reports.AssetDepreciationAsync(branches, ct),
            StandardReportKeys.DueMaintenance => await _reports.DueMaintenanceAsync(to, branches, ct: ct),
            StandardReportKeys.DailySales => await _reports.DailySalesAsync(from, to, branches, ct),
            StandardReportKeys.PartyBalances => await _reports.PartyBalancesMonthlyAsync(from, to, ct),
            _ => throw new ValidationAppException("validation.report_key_unknown")
        };
    }

    public static bool IsSupported(string reportKey) => StandardReportKeys.All.Contains(reportKey);

    private static Guid[] Scope(ActorContext actor, IReadOnlyList<Guid>? requested)
    {
        if (actor.IsSuperAdmin) return requested?.Distinct().ToArray() ?? Array.Empty<Guid>();
        var allowed = actor.AllowedBranchIds.ToHashSet();
        var picked = requested is { Count: > 0 }
            ? requested.Where(allowed.Contains).Distinct().ToArray()
            : allowed.ToArray();
        return picked.Length > 0
            ? picked
            : actor.CurrentBranchId is { } current ? new[] { current } : Array.Empty<Guid>();
    }
}
