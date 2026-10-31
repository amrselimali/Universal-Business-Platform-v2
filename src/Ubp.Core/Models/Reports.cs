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
            "report.reservation_status", ScreenCodes.ReportsCenter)
    };

    public static ReportDefinition? Find(string key) =>
        All.FirstOrDefault(r => string.Equals(r.Key, key, StringComparison.Ordinal));
}
