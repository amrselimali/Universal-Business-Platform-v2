namespace Ubp.Core.Models;

public enum ReportValueType
{
    Text = 0,
    Number = 1,
    Money = 2,
    Date = 3
}

/// <summary>عمود في تقرير قياسي (مفتاح ترجمة + نوع العرض).</summary>
public sealed record ReportColumn(string Key, string TitleKey, ReportValueType Type = ReportValueType.Text);

/// <summary>
/// جدول تقرير موحّد: مصدر واحد يخدم الشاشة والتصدير (CSV/XLSX) والطباعة.
/// القيم نصية مُنسّقة بـ InvariantCulture لتطابق الملف المُصدَّر.
/// </summary>
public sealed record ReportTable(IReadOnlyList<ReportColumn> Columns,
    IReadOnlyList<IReadOnlyList<string?>> Rows, string? Footer = null);

/// <summary>مفاتيح التقارير القياسية (8C+/التقارير المعيارية).</summary>
public static class StandardReportKeys
{
    public const string Vat = "tax.vat_summary";
    public const string ItemMargin = "sales.item_margin";
    public const string CustomerMeter = "assets.customer_meter_usage";
    public const string Reservations = "sales.reservation_status";
    public const string ShiftVariance = "treasury.shift_variance";
    public const string DeadStock = "stock.dead_stock";
    public const string SupplierPerformance = "purchasing.supplier_performance";
    public const string StockAging = "stock.aging";
    public const string StockMovements = "stock.movements";
    public const string StockFlow = "stock.flow";
    public const string StockValueByWarehouse = "stock.value_by_warehouse";
    public const string Commissions = "hr.commissions";
    public const string BookingConversion = "sales.booking_conversion";
    public const string UnappliedAdvances = "sales.unapplied_advances";
    public const string CreditLimits = "sales.credit_limits";
    public const string FollowUps = "sales.followups";
    public const string DepreciationSchedule = "assets.depreciation_schedule";
    public const string DueMaintenance = "assets.due_maintenance";
    public const string DailySales = "sales.daily";
    public const string PartyBalances = "bi.party_balances";

    public static readonly IReadOnlyList<string> All =
        [Vat, ItemMargin, CustomerMeter, Reservations, ShiftVariance, DeadStock, SupplierPerformance,
         StockAging, StockMovements, StockFlow, StockValueByWarehouse, Commissions, BookingConversion,
         UnappliedAdvances, CreditLimits, FollowUps, DepreciationSchedule, DueMaintenance, DailySales,
         PartyBalances];
}

/// <summary>معاملات تشغيل أي تقرير قياسي.</summary>
public sealed record ReportFilter(DateTime From, DateTime To, IReadOnlyList<Guid>? BranchIds = null);
