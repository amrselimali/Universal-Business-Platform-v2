namespace Ubp.Core.Models;

/// <summary>مؤشرات مبيعات لفترة (تُقرأ من sales_daily_totals لا من الفواتير).</summary>
public sealed record SalesKpi(
    decimal NetTotal,
    decimal TaxTotal,
    decimal GrossTotal,
    decimal CogsTotal,
    int InvoiceCount,
    int ReturnCount)
{
    public decimal GrossMargin => GrossTotal - CogsTotal;
    public decimal AverageBasket => InvoiceCount == 0 ? 0 : NetTotal / InvoiceCount;
}

/// <summary>لقطة لوحة المؤشرات: اليوم، الشهر، قيمة المخزون، وذمم العملاء.</summary>
public sealed record DashboardSnapshot(
    SalesKpi Today,
    SalesKpi Month,
    decimal StockValue,
    decimal Receivables);
