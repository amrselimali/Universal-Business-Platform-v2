using Dapper;
using Ubp.Core.Models;
using Ubp.Infrastructure.Data;

namespace Ubp.Infrastructure.Repositories;

public sealed partial class StandardReportRepository
{
    /// <summary>جدول الإهلاك: التكلفة/المجمع/القيمة الدفترية لكل أصل.</summary>
    public async Task<ReportTable> AssetDepreciationAsync(Guid[] branchIds,
        CancellationToken ct = default)
    {
        const string sql = """
            SELECT a.code AS Code, a.name_ar AS AssetName, a.cost AS Cost,
                   COALESCE(SUM(l.amount), 0) AS Accumulated
            FROM fixed_assets a
            LEFT JOIN asset_depreciation_lines l ON l.asset_id = a.id
            WHERE a.status <> 'DISPOSED'
              AND (@allBranches OR a.branch_id IS NULL OR a.branch_id = ANY(@branchIds))
            GROUP BY a.id, a.code, a.name_ar, a.cost
            ORDER BY a.code
            LIMIT 300
            """;
        var rows = await QueryAsync<DepRow>(sql, new
        { branchIds, allBranches = branchIds.Length == 0 }, ct);
        return new ReportTable(
        [
            new("code", "common.code"), new("name", "common.name_ar"),
            new("cost", "assets.cost", ReportValueType.Money),
            new("accumulated", "report.accumulated", ReportValueType.Money),
            new("book", "report.book_value", ReportValueType.Money)
        ],
        rows.Select(r => (IReadOnlyList<string?>)
        [r.Code, r.AssetName, Money(r.Cost), Money(r.Accumulated), Money(r.Cost - r.Accumulated)])
            .ToList(), $"report.book_total={Money(rows.Sum(r => r.Cost - r.Accumulated))}");
    }

    /// <summary>الصيانة المستحقة: زمنية أو بالعدّاد خلال نافذة الأيام.</summary>
    public async Task<ReportTable> DueMaintenanceAsync(DateOnly asOf, Guid[] branchIds, int days = 30,
        CancellationToken ct = default)
    {
        const string sql = """
            SELECT a.code AS Code, a.name_ar AS AssetName, p.kind AS Kind,
                   p.next_due_date::date AS DueDate, p.next_due_meter AS DueMeter,
                   a.current_meter_value AS CurrentMeter, a.branch_id AS BranchId
            FROM asset_maintenance_plans p
            JOIN fixed_assets a ON a.id = p.asset_id
            WHERE p.is_active AND a.status = 'ACTIVE'
              AND (@allBranches OR a.branch_id IS NULL OR a.branch_id = ANY(@branchIds))
              AND (
                   (p.kind = 'TIME' AND (p.next_due_date IS NULL
                        OR p.next_due_date <= @asOf::date + @days))
                OR (p.kind = 'METER' AND p.next_due_meter IS NOT NULL
                        AND a.current_meter_value >= p.next_due_meter))
            ORDER BY p.next_due_date NULLS LAST
            LIMIT 200
            """;
        var rows = await QueryAsync<DueRow>(sql, new
        {
            asOf = asOf.ToDateTime(TimeOnly.MinValue), days,
            branchIds, allBranches = branchIds.Length == 0
        }, ct);
        return new ReportTable(
        [
            new("code", "common.code"), new("name", "common.name_ar"),
            new("kind", "assets.plan_kind"), new("due_date", "report.due_date", ReportValueType.Date),
            new("due_meter", "report.due_meter", ReportValueType.Number),
            new("current_meter", "assets.meter", ReportValueType.Number)
        ],
        rows.Select(r => (IReadOnlyList<string?>)
        [r.Code, r.AssetName, r.Kind, Fmt(r.DueDate),
            r.DueMeter is null ? "—" : Num(r.DueMeter.Value), Num(r.CurrentMeter)]).ToList());
    }

    /// <summary>المبيعات اليومية لكل فرع (مصدر واحد: sales_daily_totals).</summary>
    public async Task<ReportTable> DailySalesAsync(DateOnly from, DateOnly to, Guid[] branchIds,
        CancellationToken ct = default)
    {
        const string sql = """
            SELECT t.day::date AS Day, b.name_ar AS BranchName, t.invoice_count AS Invoices,
                   t.return_count AS Returns, t.net_total AS Net, t.tax_total AS Tax,
                   t.gross_total AS Gross, t.cogs_total AS Cogs
            FROM sales_daily_totals t
            JOIN branches b ON b.id = t.branch_id
            WHERE t.day BETWEEN @from AND @to
              AND (@allBranches OR t.branch_id = ANY(@branchIds))
            ORDER BY t.day DESC, b.name_ar
            LIMIT 500
            """;
        var rows = await QueryAsync<DailyRow>(sql, new
        {
            from = from.ToDateTime(TimeOnly.MinValue), to = to.ToDateTime(TimeOnly.MinValue),
            branchIds, allBranches = branchIds.Length == 0
        }, ct);
        return new ReportTable(
        [
            new("day", "report.date", ReportValueType.Date), new("branch", "common.branch"),
            new("invoices", "report.invoices", ReportValueType.Number),
            new("returns", "report.returns", ReportValueType.Number),
            new("net", "report.net", ReportValueType.Money),
            new("tax", "report.tax", ReportValueType.Money),
            new("gross", "report.gross", ReportValueType.Money),
            new("margin", "report.margin", ReportValueType.Money)
        ],
        rows.Select(r => (IReadOnlyList<string?>)
        [Fmt(r.Day), r.BranchName, r.Invoices.ToString(), r.Returns.ToString(), Money(r.Net),
            Money(r.Tax), Money(r.Gross), Money(r.Gross - r.Cogs)]).ToList(),
        $"report.net_total={Money(rows.Sum(r => r.Net))}");
    }

    /// <summary>أرصدة الأطراف الشهرية من مخطط bi (قراءة فقط).</summary>
    public async Task<ReportTable> PartyBalancesMonthlyAsync(DateOnly from, DateOnly to,
        CancellationToken ct = default)
    {
        const string sql = """
            SELECT m.period_month::date AS PeriodMonth, m.party_type AS PartyType,
                   COALESCE(c.name_ar, s.name_ar, '-') AS PartyName,
                   m.debit_total AS DebitTotal, m.credit_total AS CreditTotal, m.balance AS Balance
            FROM bi.party_balances_monthly m
            LEFT JOIN customers c ON m.party_type = 'C' AND c.id = m.party_id
            LEFT JOIN suppliers s ON m.party_type = 'S' AND s.id = m.party_id
            WHERE m.period_month BETWEEN @from AND @to
            ORDER BY m.period_month DESC, ABS(m.balance) DESC
            LIMIT 500
            """;
        var rows = await QueryAsync<PartyRow>(sql, new
        {
            from = from.ToDateTime(TimeOnly.MinValue), to = to.ToDateTime(TimeOnly.MinValue)
        }, ct);
        return new ReportTable(
        [
            new("period", "report.period", ReportValueType.Date),
            new("party", "report.party"), new("debit", "report.debit", ReportValueType.Money),
            new("credit", "report.credit", ReportValueType.Money),
            new("balance", "report.balance", ReportValueType.Money)
        ],
        rows.Select(r => (IReadOnlyList<string?>)
        [Fmt(r.PeriodMonth), r.PartyName, Money(r.DebitTotal), Money(r.CreditTotal),
            Money(r.Balance)]).ToList());
    }

    private sealed class DepRow
    {
        public string Code { get; set; } = "";
        public string AssetName { get; set; } = "";
        public decimal Cost { get; set; }
        public decimal Accumulated { get; set; }
    }

    private sealed class DueRow
    {
        public string Code { get; set; } = "";
        public string AssetName { get; set; } = "";
        public string Kind { get; set; } = "";
        public DateTime? DueDate { get; set; }
        public decimal? DueMeter { get; set; }
        public decimal CurrentMeter { get; set; }
    }

    private sealed class DailyRow
    {
        public DateTime Day { get; set; }
        public string BranchName { get; set; } = "";
        public int Invoices { get; set; }
        public int Returns { get; set; }
        public decimal Net { get; set; }
        public decimal Tax { get; set; }
        public decimal Gross { get; set; }
        public decimal Cogs { get; set; }
    }

    private sealed class PartyRow
    {
        public DateTime PeriodMonth { get; set; }
        public string PartyType { get; set; } = "";
        public string PartyName { get; set; } = "";
        public decimal DebitTotal { get; set; }
        public decimal CreditTotal { get; set; }
        public decimal Balance { get; set; }
    }
}
