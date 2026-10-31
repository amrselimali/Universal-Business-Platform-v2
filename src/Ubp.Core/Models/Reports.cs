using Ubp.Core.Security;

namespace Ubp.Core.Models;

/// <summary>فلتر تقرير حفظه المستخدم (الترحيل 0039).</summary>
public sealed class SavedReportFilter
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public string ReportKey { get; set; } = "";
    public string FilterName { get; set; } = "";
    /// <summary>كائن JSON مسطّح بمفاتيح الفلتر وقيمها (لا استعلامات ولا SQL).</summary>
    public string FiltersJson { get; set; } = "{}";
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>تعريف تقرير معتمد داخل مركز التقارير، مربوط بشاشة تحكم صلاحيتها.</summary>
public sealed record ReportDefinition(string Key, string Route, string TitleKey, string ScreenCode);

/// <summary>سجل التقارير المعتمد — مصدر واحد لمركز التقارير (لا يُبنى من الواجهة).</summary>
public static class ReportCatalog
{
    public static readonly IReadOnlyList<ReportDefinition> All = new List<ReportDefinition>
    {
        new("stock.balances", "/reports/stock", "screen.stock_reports", ScreenCodes.StockReports),
        new("accounting.trial_balance", "/reports/accounting/trial-balance",
            "screen.trial_balance_report", ScreenCodes.TrialBalanceReport),
        new("accounting.ledger", "/reports/accounting/ledger", "screen.ledger_report",
            ScreenCodes.LedgerReport),
        new("accounting.party_statement", "/reports/accounting/party-statement",
            "screen.party_statement_report", ScreenCodes.PartyStatementReport),
        new("accounting.cost_centers", "/reports/accounting/cost-centers",
            "screen.cost_center_report", ScreenCodes.CostCenterReport),
        new("accounting.stock_vs_gl", "/reports/accounting/stock-vs-gl",
            "screen.stock_vs_gl_report", ScreenCodes.StockVsGlReport),
        new("sales.aging", "/reports/aging", "screen.aging_report", ScreenCodes.AgingReport),
        new(StandardReportKeys.Vat, "/reports/standard/tax.vat_summary", "report.vat",
            ScreenCodes.ReportsCenter),
        new(StandardReportKeys.ItemMargin, "/reports/standard/sales.item_margin", "report.item_margin",
            ScreenCodes.ReportsCenter),
        new(StandardReportKeys.CustomerMeter, "/reports/standard/assets.customer_meter_usage",
            "report.customer_meter", ScreenCodes.ReportsCenter),
        new(StandardReportKeys.Reservations, "/reports/standard/sales.reservation_status",
            "report.reservation_status", ScreenCodes.ReportsCenter),
        new(StandardReportKeys.ShiftVariance, "/reports/standard/treasury.shift_variance",
            "report.shift_variance", ScreenCodes.ReportsCenter),
        new(StandardReportKeys.DeadStock, "/reports/standard/stock.dead_stock", "report.dead_stock",
            ScreenCodes.ReportsCenter),
        new(StandardReportKeys.SupplierPerformance, "/reports/standard/purchasing.supplier_performance",
            "report.supplier_performance", ScreenCodes.ReportsCenter),
        new(StandardReportKeys.StockAging, "/reports/standard/stock.aging", "report.stock_aging",
            ScreenCodes.ReportsCenter),
        new(StandardReportKeys.StockMovements, "/reports/standard/stock.movements",
            "report.stock_movements", ScreenCodes.ReportsCenter),
        new(StandardReportKeys.StockFlow, "/reports/standard/stock.flow", "report.stock_flow",
            ScreenCodes.ReportsCenter),
        new(StandardReportKeys.StockValueByWarehouse, "/reports/standard/stock.value_by_warehouse",
            "report.stock_value", ScreenCodes.ReportsCenter),
        new(StandardReportKeys.Commissions, "/reports/standard/hr.commissions", "report.commissions",
            ScreenCodes.ReportsCenter),
        new(StandardReportKeys.BookingConversion, "/reports/standard/sales.booking_conversion",
            "report.booking_conversion", ScreenCodes.ReportsCenter),
        new(StandardReportKeys.UnappliedAdvances, "/reports/standard/sales.unapplied_advances",
            "report.unapplied_advances", ScreenCodes.ReportsCenter),
        new(StandardReportKeys.CreditLimits, "/reports/standard/sales.credit_limits",
            "report.credit_limits", ScreenCodes.ReportsCenter),
        new(StandardReportKeys.FollowUps, "/reports/standard/sales.followups", "report.followups",
            ScreenCodes.ReportsCenter),
        new(StandardReportKeys.DepreciationSchedule, "/reports/standard/assets.depreciation_schedule",
            "report.depreciation_schedule", ScreenCodes.ReportsCenter),
        new(StandardReportKeys.DueMaintenance, "/reports/standard/assets.due_maintenance",
            "report.due_maintenance", ScreenCodes.ReportsCenter),
        new(StandardReportKeys.DailySales, "/reports/standard/sales.daily", "report.daily_sales",
            ScreenCodes.ReportsCenter),
        new(StandardReportKeys.PartyBalances, "/reports/standard/bi.party_balances",
            "report.party_balances", ScreenCodes.ReportsCenter)
    };

    public static ReportDefinition? Find(string key) =>
        All.FirstOrDefault(r => string.Equals(r.Key, key, StringComparison.Ordinal));
}
