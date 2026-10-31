using Dapper;
using Ubp.Core.Models;
using Ubp.Infrastructure.Data;

namespace Ubp.Infrastructure.Repositories;

public sealed partial class StandardReportRepository
{
    /// <summary>عمولات الموظفين مجمّعة بالشهر والموظف (مصدرها employee_commissions).</summary>
    public async Task<ReportTable> CommissionsAsync(DateOnly from, DateOnly to,
        CancellationToken ct = default)
    {
        const string sql = """
            SELECT e.name_ar AS EmployeeName, c.period_month::date AS PeriodMonth,
                   c.basis AS Basis, SUM(c.base_amount) AS BaseAmount,
                   SUM(c.amount) AS Amount, COUNT(*)::int AS Lines
            FROM employee_commissions c
            JOIN employees e ON e.id = c.employee_id
            WHERE c.period_month::date BETWEEN @from AND @to
            GROUP BY e.name_ar, c.period_month, c.basis
            ORDER BY c.period_month DESC, SUM(c.amount) DESC
            LIMIT 300
            """;
        var rows = await QueryAsync<CommissionRow>(sql, new
        {
            from = from.ToDateTime(TimeOnly.MinValue), to = to.ToDateTime(TimeOnly.MinValue)
        }, ct);
        return new ReportTable(
        [
            new("employee", "report.employee"), new("period", "report.period", ReportValueType.Date),
            new("basis", "report.commission_basis"),
            new("base", "report.base_amount", ReportValueType.Money),
            new("amount", "report.commission_amount", ReportValueType.Money),
            new("lines", "report.documents", ReportValueType.Number)
        ],
        rows.Select(r => (IReadOnlyList<string?>)
        [r.EmployeeName, Fmt(r.PeriodMonth), r.Basis, Money(r.BaseAmount), Money(r.Amount),
            r.Lines.ToString()]).ToList(),
        $"report.commission_total={Money(rows.Sum(r => r.Amount))}");
    }

    /// <summary>تحويل الحجوزات: عدد وقيمة الحجوزات حسب الحالة والشهر.</summary>
    public async Task<ReportTable> BookingConversionAsync(DateOnly from, DateOnly to,
        Guid[] branchIds, CancellationToken ct = default)
    {
        const string sql = """
            SELECT to_char(b.booking_date, 'YYYY-MM') AS PeriodMonth, b.status AS Status,
                   COUNT(*)::int AS Bookings, SUM(b.total_amount) AS Amount,
                   SUM(b.deposit_amount) AS Deposits
            FROM bookings b
            WHERE b.booking_date BETWEEN @from AND @to
              AND (@allBranches OR b.branch_id = ANY(@branchIds))
            GROUP BY 1, 2 ORDER BY 1 DESC, 2
            """;
        var rows = await QueryAsync<ConversionRow>(sql, new
        {
            from = from.ToDateTime(TimeOnly.MinValue), to = to.ToDateTime(TimeOnly.MinValue),
            branchIds, allBranches = branchIds.Length == 0
        }, ct);
        return new ReportTable(
        [
            new("period", "report.period"), new("status", "assets.status"),
            new("bookings", "report.bookings", ReportValueType.Number),
            new("amount", "report.gross", ReportValueType.Money),
            new("deposits", "report.deposit", ReportValueType.Money)
        ],
        rows.Select(r => (IReadOnlyList<string?>)
        [r.PeriodMonth, r.Status, r.Bookings.ToString(), Money(r.Amount), Money(r.Deposits)])
            .ToList());
    }

    /// <summary>المقدمات غير المخصصة: أرصدة دائنة قائمة للعملاء (مصدرها party_balances).</summary>
    public async Task<ReportTable> UnappliedAdvancesAsync(Guid[] branchIds,
        CancellationToken ct = default)
    {
        const string sql = """
            SELECT c.name_ar AS CustomerName, SUM(pb.credit - pb.debit) AS Balance
            FROM party_balances pb
            JOIN customers c ON c.id = pb.party_id
            WHERE pb.party_type = 'C'
              AND (@allBranches OR pb.branch_id = ANY(@branchIds))
            GROUP BY c.id, c.name_ar
            HAVING SUM(pb.credit - pb.debit) > 0
            ORDER BY SUM(pb.credit - pb.debit) DESC
            LIMIT 200
            """;
        var rows = await QueryAsync<AdvanceRow>(sql, new
        { branchIds, allBranches = branchIds.Length == 0 }, ct);
        return new ReportTable(
        [new("customer", "report.customer"), new("balance", "report.advance", ReportValueType.Money)],
        rows.Select(r => (IReadOnlyList<string?>)[r.CustomerName, Money(r.Balance)]).ToList(),
        $"report.advance_total={Money(rows.Sum(r => r.Balance))}");
    }

    /// <summary>استغلال حدود الائتمان لكل عميل/فرع.</summary>
    public async Task<ReportTable> CreditLimitsAsync(Guid[] branchIds, CancellationToken ct = default)
    {
        const string sql = """
            SELECT c.name_ar AS CustomerName, b.name_ar AS BranchName, l.credit_limit AS LimitAmount,
                   COALESCE(SUM(pb.debit - pb.credit), 0) AS Exposure
            FROM customer_branch_limits l
            JOIN customers c ON c.id = l.customer_id
            JOIN branches b ON b.id = l.branch_id
            LEFT JOIN party_balances pb ON pb.party_id = l.customer_id AND pb.party_type = 'C'
                 AND pb.branch_id = l.branch_id
            WHERE l.is_active AND (@allBranches OR l.branch_id = ANY(@branchIds))
            GROUP BY c.name_ar, b.name_ar, l.credit_limit
            ORDER BY l.credit_limit DESC
            LIMIT 200
            """;
        var rows = await QueryAsync<LimitRow>(sql, new
        { branchIds, allBranches = branchIds.Length == 0 }, ct);
        return new ReportTable(
        [
            new("customer", "report.customer"), new("branch", "common.branch"),
            new("limit", "report.credit_limit", ReportValueType.Money),
            new("exposure", "report.exposure", ReportValueType.Money),
            new("available", "report.available", ReportValueType.Money)
        ],
        rows.Select(r => (IReadOnlyList<string?>)
        [r.CustomerName, r.BranchName, Money(r.LimitAmount), Money(r.Exposure),
            Money(r.LimitAmount - r.Exposure)]).ToList());
    }

    /// <summary>المتابعات حسب الحالة والموظف.</summary>
    public async Task<ReportTable> FollowUpsAsync(DateOnly from, DateOnly to,
        CancellationToken ct = default)
    {
        const string sql = """
            SELECT f.status AS Status, COALESCE(u.username, '-') AS Assignee,
                   COUNT(*)::int AS Items, MIN(f.due_at)::date AS OldestDue,
                   MAX(f.completed_at)::date AS LastCompleted
            FROM follow_ups f
            LEFT JOIN users u ON u.id = f.assigned_user_id
            WHERE f.due_at::date BETWEEN @from AND @to
            GROUP BY f.status, u.username
            ORDER BY f.status, COUNT(*) DESC
            """;
        var rows = await QueryAsync<FollowRow>(sql, new
        {
            from = from.ToDateTime(TimeOnly.MinValue), to = to.ToDateTime(TimeOnly.MinValue)
        }, ct);
        return new ReportTable(
        [
            new("status", "assets.status"), new("assignee", "report.assignee"),
            new("items", "report.items", ReportValueType.Number),
            new("oldest", "report.oldest_due", ReportValueType.Date),
            new("last", "report.last_completed", ReportValueType.Date)
        ],
        rows.Select(r => (IReadOnlyList<string?>)
        [r.Status, r.Assignee, r.Items.ToString(), Fmt(r.OldestDue), Fmt(r.LastCompleted)])
            .ToList());
    }

    private sealed class CommissionRow
    {
        public string EmployeeName { get; set; } = "";
        public DateTime PeriodMonth { get; set; }
        public string Basis { get; set; } = "";
        public decimal BaseAmount { get; set; }
        public decimal Amount { get; set; }
        public int Lines { get; set; }
    }

    private sealed class ConversionRow
    {
        public string PeriodMonth { get; set; } = "";
        public string Status { get; set; } = "";
        public int Bookings { get; set; }
        public decimal Amount { get; set; }
        public decimal Deposits { get; set; }
    }

    private sealed class AdvanceRow
    {
        public string CustomerName { get; set; } = "";
        public decimal Balance { get; set; }
    }

    private sealed class LimitRow
    {
        public string CustomerName { get; set; } = "";
        public string BranchName { get; set; } = "";
        public decimal LimitAmount { get; set; }
        public decimal Exposure { get; set; }
    }

    private sealed class FollowRow
    {
        public string Status { get; set; } = "";
        public string Assignee { get; set; } = "";
        public int Items { get; set; }
        public DateTime? OldestDue { get; set; }
        public DateTime? LastCompleted { get; set; }
    }
}
