using Dapper;
using Ubp.Core.Models;
using Ubp.Infrastructure.Data;

namespace Ubp.Infrastructure.Repositories;

public interface IAccountingReportRepository
{
    Task<TrialBalanceReport> TrialBalanceAsync(DateTime from, DateTime to, Guid[] branchIds,
        CancellationToken ct = default);
    Task<LedgerPage> LedgerAsync(LedgerRequest request, CancellationToken ct = default);
    Task<PartyStatementReport> PartyStatementAsync(char partyType, Guid partyId, DateTime from,
        DateTime to, Guid[] branchIds, CancellationToken ct = default);
    Task<IReadOnlyList<CostCenterAnalysisRow>> CostCenterAsync(DateTime from, DateTime to,
        Guid[] branchIds, CancellationToken ct = default);
    Task<IReadOnlyList<StockVsGlRow>> StockVsGlAsync(Guid[] branchIds, CancellationToken ct = default);
}

public sealed partial class AccountingReportRepository : IAccountingReportRepository
{
    private readonly IDbConnectionFactory _factory;

    public AccountingReportRepository(IDbConnectionFactory factory) => _factory = factory;

    /// <summary>ميزان المراجعة من الأرصدة الشهرية مباشرة — لا يلمس بنود القيود.</summary>
    public async Task<TrialBalanceReport> TrialBalanceAsync(DateTime from, DateTime to, Guid[] branchIds,
        CancellationToken ct = default)
    {
        const string sql = """
            WITH bounds AS (
                SELECT date_trunc('month', @from::date)::date AS month_from,
                       date_trunc('month', @to::date)::date   AS month_to
            ), bal AS (
                SELECT b.account_id,
                       sum(CASE WHEN b.period_month <  x.month_from THEN b.debit  ELSE 0 END) AS opening_debit,
                       sum(CASE WHEN b.period_month <  x.month_from THEN b.credit ELSE 0 END) AS opening_credit,
                       sum(CASE WHEN b.period_month >= x.month_from THEN b.debit  ELSE 0 END) AS debit,
                       sum(CASE WHEN b.period_month >= x.month_from THEN b.credit ELSE 0 END) AS credit
                FROM gl_balances b CROSS JOIN bounds x
                WHERE b.period_month <= x.month_to
                  AND (cardinality(@branchIds) = 0 OR b.branch_id = ANY(@branchIds))
                GROUP BY b.account_id
            )
            SELECT a.id AS AccountId, a.code AS Code, a.name_ar AS NameAr, a.name_en AS NameEn,
                   a.account_type AS AccountType, a.level AS Level,
                   coalesce(bal.opening_debit, 0)  AS OpeningDebit,
                   coalesce(bal.opening_credit, 0) AS OpeningCredit,
                   coalesce(bal.debit, 0)  AS Debit,
                   coalesce(bal.credit, 0) AS Credit
            FROM accounts a LEFT JOIN bal ON bal.account_id = a.id
            WHERE a.is_active AND NOT a.is_group
              AND (coalesce(bal.opening_debit, 0) <> 0 OR coalesce(bal.opening_credit, 0) <> 0
                   OR coalesce(bal.debit, 0) <> 0 OR coalesce(bal.credit, 0) <> 0)
            ORDER BY a.code
            """;

        await using var connection = await _factory.OpenAsync(ct);
        var rows = await connection.QueryAsync<TrialBalanceRow>(new CommandDefinition(sql,
            new { from, to, branchIds }, cancellationToken: ct));
        return new TrialBalanceReport
        {
            From = from, To = to, BranchIds = branchIds, Rows = rows.ToList()
        };
    }
}
