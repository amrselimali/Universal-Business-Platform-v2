using Dapper;
using Ubp.Core.Models;
using Ubp.Infrastructure.Data;

namespace Ubp.Infrastructure.Repositories;

public interface IStandardReportRepository
{
    /// <summary>ملخص الضريبة (مخرجات/مدخلات) لكل فترة ومعدل — من أسطر الفواتير المرحّلة.</summary>
    Task<ReportTable> VatSummaryAsync(DateOnly from, DateOnly to, Guid[] branchIds,
        CancellationToken ct = default);
    /// <summary>هامش الربح لكل صنف: إيراد الفاتورة مقابل تكلفة التسليم (نفس ربط COGS).</summary>
    Task<ReportTable> ItemMarginAsync(DateOnly from, DateOnly to, Guid[] branchIds,
        CancellationToken ct = default);
    /// <summary>استهلاك العدادات لكل عميل (قراءات SALES).</summary>
    Task<ReportTable> CustomerMeterUsageAsync(DateOnly from, DateOnly to, Guid[] branchIds,
        CancellationToken ct = default);
    /// <summary>حالة الحجوزات والدفعات (محجوز/مستهلك/متبقٍ + الدفعة).</summary>
    Task<ReportTable> ReservationStatusAsync(DateOnly from, DateOnly to, Guid[] branchIds,
        bool onlyRemaining = true, CancellationToken ct = default);
}

/// <summary>
/// تقارير قياسية: كل تقرير جملة SQL واحدة على المصدر المعتمد، ويُعاد جدول موحّد
/// (أعمدة + صفوف) يخدم الشاشة والتصدير والطباعة بلا تكرار.
/// </summary>
public sealed partial class StandardReportRepository : IStandardReportRepository
{
    private readonly IDbConnectionFactory _factory;

    public StandardReportRepository(IDbConnectionFactory factory) => _factory = factory;

    public async Task<ReportTable> VatSummaryAsync(DateOnly from, DateOnly to, Guid[] branchIds,
        CancellationToken ct = default)
    {
        const string sql = """
            SELECT to_char(d.doc_date, 'YYYY-MM') AS Period,
                   CASE d.doc_type WHEN 'sales_invoice' THEN 'S' ELSE 'P' END AS Direction,
                   COALESCE(t.rate, 0) AS Rate,
                   SUM(l.net_amount) AS Net, SUM(l.tax_amount) AS Tax,
                   SUM(l.total_amount) AS Gross, COUNT(DISTINCT d.id)::int AS Docs
            FROM commercial_document_lines l
            JOIN commercial_documents d ON d.id = l.document_id
            LEFT JOIN taxes t ON t.id = l.tax_id
            WHERE d.status = 'posted'
              AND d.doc_type IN ('sales_invoice', 'purchase_invoice')
              AND d.doc_date BETWEEN @from AND @to
              AND (@allBranches OR d.branch_id = ANY(@branchIds))
            GROUP BY 1, 2, 3
            ORDER BY 1 DESC, 2, 3 DESC
            """;
        await using var connection = await _factory.OpenAsync(ct);
        var rows = (await connection.QueryAsync<VatRow>(new CommandDefinition(sql, new
        {
            from = from.ToDateTime(TimeOnly.MinValue), to = to.ToDateTime(TimeOnly.MinValue),
            branchIds, allBranches = branchIds.Length == 0
        }, cancellationToken: ct))).ToList();

        var columns = new List<ReportColumn>
        {
            new("period", "report.period"),
            new("direction", "report.direction"),
            new("rate", "report.tax_rate", ReportValueType.Number),
            new("net", "report.net", ReportValueType.Money),
            new("tax", "report.tax", ReportValueType.Money),
            new("gross", "report.gross", ReportValueType.Money),
            new("docs", "report.documents", ReportValueType.Number)
        };
        var data = rows.Select(r => (IReadOnlyList<string?>)
        [
            r.Period, r.Direction == "S" ? "sales" : "purchase",
            Num(r.Rate), Money(r.Net), Money(r.Tax), Money(r.Gross), r.Docs.ToString()
        ]).ToList();

        return new ReportTable(columns, data, $"report.tax_total={Money(rows.Sum(r => r.Tax))}");
    }

    private sealed class VatRow
    {
        public string Period { get; set; } = "";
        public string Direction { get; set; } = "";
        public decimal Rate { get; set; }
        public decimal Net { get; set; }
        public decimal Tax { get; set; }
        public decimal Gross { get; set; }
        public int Docs { get; set; }
    }
}
