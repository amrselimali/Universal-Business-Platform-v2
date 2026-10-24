using Dapper;
using Ubp.Core.Models;
using Ubp.Infrastructure.Data;

namespace Ubp.Infrastructure.Repositories;

public sealed partial class AccountingReportRepository
{
    /// <summary>
    /// كشف الأستاذ: رصيد سابق من gl_balances + حركات الفترة بـ keyset pagination
    /// على (entry_date, id) — لا OFFSET ولا إعادة حساب للأرصدة على كل صفحة.
    /// </summary>
    public async Task<LedgerPage> LedgerAsync(LedgerRequest request, CancellationToken ct = default)
    {
        var take = request.PageSize is < 1 or > 200 ? 50 : request.PageSize;
        var cursorId = request.Cursor?.LineId ?? Guid.Empty;

        await using var connection = await _factory.OpenAsync(ct);

        var account = await connection.QuerySingleOrDefaultAsync<AccountHead>(new CommandDefinition(
            "SELECT id AS Id, code AS Code, path AS Path, name_ar AS NameAr FROM accounts WHERE id = @id",
            new { id = request.AccountId }, cancellationToken: ct));
        if (account is null) return new LedgerPage();

        var opening = await connection.ExecuteScalarAsync<decimal>(new CommandDefinition(
            "SELECT fn_gl_opening_balance(@accountId, @branchId, @from::date, @includeChildren)",
            new { accountId = request.AccountId, branchId = request.BranchId, from = request.From,
                  includeChildren = request.IncludeChildren }, cancellationToken: ct));

        var totals = await connection.QuerySingleAsync<LedgerTotals>(new CommandDefinition("""
            SELECT coalesce(sum(b.debit), 0) AS Debit,
                   coalesce(sum(b.credit), 0) AS Credit
            FROM gl_balances b
            JOIN accounts a ON a.id = b.account_id
            WHERE b.period_month BETWEEN date_trunc('month', @from::date)::date
                                     AND date_trunc('month', @to::date)::date
              AND (CASE WHEN @includeChildren THEN a.path LIKE @path || '%'
                        ELSE b.account_id = @accountId END)
              AND (@branchId::uuid IS NULL OR b.branch_id = @branchId::uuid)
            """, new
        {
            from = request.From, to = request.To, path = account.Path,
            accountId = request.AccountId, includeChildren = request.IncludeChildren,
            branchId = request.BranchId
        }, cancellationToken: ct));

        var rows = (await connection.QueryAsync<LedgerRow>(new CommandDefinition("""
            SELECT l.id AS LineId, e.id AS EntryId, e.entry_no AS EntryNo,
                   l.entry_date AS EntryDate, j.code AS JournalCode, l.description AS Description,
                   e.reference AS Reference, a.code AS AccountCode, a.name_ar AS AccountName,
                   cc.name_ar AS CostCenterName,
                   coalesce(cu.name_ar, su.name_ar) AS PartyName,
                   l.debit AS Debit, l.credit AS Credit
            FROM journal_entry_lines l
            JOIN journal_entries e ON e.id = l.entry_id AND e.status = 'posted'
            JOIN accounts a ON a.id = l.account_id
            JOIN journals j ON j.id = e.journal_id
            LEFT JOIN cost_centers cc ON cc.id = l.cost_center_id
            LEFT JOIN customers cu ON cu.id = l.customer_id
            LEFT JOIN suppliers su ON su.id = l.supplier_id
            WHERE l.is_posted
              AND l.entry_date BETWEEN @from::date AND @to::date
              AND (CASE WHEN @includeChildren THEN a.path LIKE @path || '%'
                        ELSE l.account_id = @accountId END)
              AND (@branchId::uuid IS NULL OR l.branch_id = @branchId::uuid)
              AND (@cursorDate::date IS NULL
                   OR (l.entry_date, l.id) > (@cursorDate::date, @cursorId::uuid))
            ORDER BY l.entry_date, l.id
            LIMIT @take
            """, new
        {
            from = request.From, to = request.To, path = account.Path,
            accountId = request.AccountId, includeChildren = request.IncludeChildren,
            branchId = request.BranchId, cursorDate = request.Cursor?.EntryDate, cursorId,
            take = take + 1
        }, cancellationToken: ct))).ToList();

        var hasMore = rows.Count > take;
        if (hasMore) rows.RemoveAt(rows.Count - 1);

        var running = request.Cursor?.Balance ?? opening;
        foreach (var row in rows)
        {
            running += row.Debit - row.Credit;
            row.RunningBalance = running;
        }

        return new LedgerPage
        {
            AccountId = request.AccountId,
            AccountCode = account.Code,
            AccountName = account.NameAr,
            From = request.From,
            To = request.To,
            OpeningBalance = opening,
            PeriodDebit = totals.Debit,
            PeriodCredit = totals.Credit,
            Rows = rows,
            HasMore = hasMore,
            NextCursor = rows.Count == 0
                ? null
                : new LedgerCursor
                {
                    EntryDate = rows[^1].EntryDate, LineId = rows[^1].LineId,
                    Balance = rows[^1].RunningBalance
                }.ToToken()
        };
    }

    private sealed class AccountHead
    {
        public Guid Id { get; set; }
        public string Code { get; set; } = "";
        public string Path { get; set; } = "/";
        public string NameAr { get; set; } = "";
    }

    private sealed class LedgerTotals
    {
        public decimal Debit { get; set; }
        public decimal Credit { get; set; }
    }
}
