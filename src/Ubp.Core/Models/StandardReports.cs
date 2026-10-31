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

    public static readonly IReadOnlyList<string> All = [Vat, ItemMargin, CustomerMeter, Reservations];
}

/// <summary>معاملات تشغيل أي تقرير قياسي.</summary>
public sealed record ReportFilter(DateTime From, DateTime To, IReadOnlyList<Guid>? BranchIds = null);
