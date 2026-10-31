using Dapper;
using Ubp.Core.Models;
using Ubp.Infrastructure.Data;

namespace Ubp.Infrastructure.Repositories;

public sealed partial class AccountingReportRepository
{
    public async Task<PartyStatementReport> PartyStatementAsync(char partyType, Guid partyId,
        DateTime from, DateTime to, Guid[] branchIds, CancellationToken ct = default)
    {
        await using var connection = await _factory.OpenAsync(ct);

        var name = partyType == 'C'
            ? await connection.ExecuteScalarAsync<string?>(new CommandDefinition(
                "SELECT name_ar FROM customers WHERE id = @partyId", new { partyId }, cancellationToken: ct))
            : await connection.ExecuteScalarAsync<string?>(new CommandDefinition(
                "SELECT name_ar FROM suppliers WHERE id = @partyId", new { partyId }, cancellationToken: ct));

        var report = new PartyStatementReport
        {
            PartyType = partyType, PartyId = partyId, PartyName = name ?? "", From = from, To = to
        };
        if (name is null) return report;

        report.OpeningBalance = await connection.ExecuteScalarAsync<decimal>(new CommandDefinition("""
            SELECT coalesce(sum(debit - credit), 0) FROM party_balances
            WHERE party_type = @partyType AND party_id = @partyId
              AND period_month < date_trunc('month', @from::date)::date
              AND (cardinality(@branchIds) = 0 OR branch_id = ANY(@branchIds))
            """, new { partyType = partyType.ToString(), partyId, from, branchIds },
            cancellationToken: ct));

        var rows = (await connection.QueryAsync<PartyStatementRow>(new CommandDefinition("""
            SELECT e.id AS EntryId, e.entry_no AS EntryNo, l.entry_date AS EntryDate,
                   l.description AS Description, e.reference AS Reference,
                   a.code AS AccountCode, a.name_ar AS AccountName,
                   l.debit AS Debit, l.credit AS Credit
            FROM journal_entry_lines l
            JOIN journal_entries e ON e.id = l.entry_id AND e.status = 'posted'
            JOIN accounts a ON a.id = l.account_id
            WHERE l.is_posted
              AND l.entry_date BETWEEN @from::date AND @to::date
              AND ((@partyType = 'C' AND l.customer_id = @partyId)
                   OR (@partyType = 'S' AND l.supplier_id = @partyId))
              AND (cardinality(@branchIds) = 0 OR l.branch_id = ANY(@branchIds))
            ORDER BY l.entry_date, l.id
            LIMIT 500
            """, new { partyType = partyType.ToString(), partyId, from, to, branchIds },
            cancellationToken: ct))).ToList();

        var running = report.OpeningBalance;
        foreach (var row in rows)
        {
            running += row.Debit - row.Credit;
            row.RunningBalance = running;
        }

        report.Rows = rows;
        report.TotalDebit = rows.Sum(r => r.Debit);
        report.TotalCredit = rows.Sum(r => r.Credit);
        return report;
    }

    public async Task<IReadOnlyList<CostCenterAnalysisRow>> CostCenterAsync(DateTime from, DateTime to,
        Guid[] branchIds, CancellationToken ct = default)
    {
        await using var connection = await _factory.OpenAsync(ct);
        var rows = await connection.QueryAsync<CostCenterAnalysisRow>(new CommandDefinition("""
            SELECT b.cost_center_id AS CostCenterId, c.code AS Code, c.name_ar AS Name,
                   coalesce(c.level, 1)::smallint AS Level,
                   sum(b.debit) AS Debit, sum(b.credit) AS Credit
            FROM gl_balances b
            LEFT JOIN cost_centers c ON c.id = b.cost_center_id
            WHERE b.period_month BETWEEN date_trunc('month', @from::date)::date
                                     AND date_trunc('month', @to::date)::date
              AND (cardinality(@branchIds) = 0 OR b.branch_id = ANY(@branchIds))
            GROUP BY b.cost_center_id, c.code, c.name_ar, c.level
            ORDER BY c.code NULLS LAST
            """, new { from, to, branchIds }, cancellationToken: ct));
        return rows.ToList();
    }

    public async Task<IReadOnlyList<StockVsGlRow>> StockVsGlAsync(Guid[] branchIds,
        CancellationToken ct = default)
    {
        await using var connection = await _factory.OpenAsync(ct);
        var rows = await connection.QueryAsync<StockVsGlRow>(new CommandDefinition("""
            WITH stock AS (
                SELECT i.category_id, sum(sb.inventory_value) AS value
                FROM stock_balances sb
                JOIN items i ON i.id = sb.item_id
                GROUP BY i.category_id
            ), gl AS (
                SELECT m.category_id, sum(b.debit - b.credit) AS balance
                FROM gl_balances b
                JOIN item_category_accounts m ON m.inventory_account_id = b.account_id
                WHERE (cardinality(@branchIds) = 0 OR b.branch_id = ANY(@branchIds))
                GROUP BY m.category_id
            )
            SELECT c.id AS CategoryId, c.name_ar AS CategoryName,
                   a.code AS AccountCode, a.name_ar AS AccountName,
                   coalesce(stock.value, 0) AS StockValue,
                   coalesce(gl.balance, 0) AS GlBalance
            FROM item_categories c
            LEFT JOIN stock ON stock.category_id = c.id
            LEFT JOIN gl ON gl.category_id = c.id
            LEFT JOIN item_category_accounts m ON m.category_id = c.id
            LEFT JOIN accounts a ON a.id = m.inventory_account_id
            WHERE c.is_active
            ORDER BY c.code
            """, new { branchIds }, cancellationToken: ct));
        return rows.ToList();
    }
}
