using Dapper;
using Ubp.Core.Models;

namespace Ubp.Infrastructure.Repositories;

public sealed partial class AccountingReportRepository
{
    /// <summary>
    /// الحسابات المرتبطة بمستندات (مصدر غير يدوي): إجمالي مدين/دائن قيود المستندات لكل حساب
    /// مقابل أرصدة الأستاذ (gl_balances)، والفرق يمثل قيوداً يدوية أو ترحيلاً ناقصاً — 0080.
    /// </summary>
    public async Task<IReadOnlyList<DocumentAccountRow>> DocumentAccountsAsync(
        Guid[] branchIds, CancellationToken ct = default)
    {
        const string sql = """
            SELECT a.id AS AccountId, a.code AS Code, a.name_ar AS NameAr,
                   COALESCE(d.debit, 0) AS DocumentDebit,
                   COALESCE(d.credit, 0) AS DocumentCredit,
                   COALESCE(g.debit, 0) AS GlDebit,
                   COALESCE(g.credit, 0) AS GlCredit
            FROM accounts a
            JOIN (
                SELECT l.account_id, SUM(l.debit) AS debit, SUM(l.credit) AS credit
                FROM journal_entry_lines l
                JOIN journal_entries e ON e.id = l.entry_id
                WHERE e.status = 'posted' AND e.source_type <> 'manual'
                  AND (@allBranches OR e.branch_id = ANY(@branchIds))
                GROUP BY l.account_id
            ) d ON d.account_id = a.id
            LEFT JOIN (
                SELECT account_id, SUM(debit) AS debit, SUM(credit) AS credit
                FROM gl_balances
                WHERE (@allBranches OR branch_id = ANY(@branchIds))
                GROUP BY account_id
            ) g ON g.account_id = a.id
            ORDER BY a.code
            """;
        await using var connection = await _factory.OpenAsync(ct);
        return (await connection.QueryAsync<DocumentAccountRow>(new CommandDefinition(sql,
            new { branchIds, allBranches = branchIds.Length == 0 }, cancellationToken: ct)))
            .ToList();
    }
}
