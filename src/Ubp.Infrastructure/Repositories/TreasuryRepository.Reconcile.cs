using Dapper;
using Npgsql;
using Ubp.Core.Models;
using Ubp.Core.Results;

namespace Ubp.Infrastructure.Repositories;

public sealed partial class TreasuryRepository
{
    /// <summary>
    /// أرصدة الحسابات الوسيطة (بطاقات/محافظ/شيكات) من الأرصدة الشهرية للأستاذ،
    /// بلا تكرار عند مشاركة أكثر من طريقة دفع لنفس الحساب.
    /// </summary>
    public async Task<IReadOnlyList<ClearingBalance>> ListClearingBalancesAsync(Guid? branchId,
        CancellationToken ct = default)
    {
        await using var connection = await _factory.OpenAsync(ct);
        return (await connection.QueryAsync<ClearingBalance>(new CommandDefinition("""
            SELECT a.id AS AccountId, a.code AS AccountCode, a.name_ar AS AccountName,
                   COALESCE(SUM(g.debit), 0) AS Debit, COALESCE(SUM(g.credit), 0) AS Credit
            FROM (SELECT DISTINCT clearing_account_id AS id FROM treasury_methods
                  WHERE is_active AND clearing_account_id IS NOT NULL) m
            JOIN accounts a ON a.id = m.id
            LEFT JOIN gl_balances g
                   ON g.account_id = m.id
                  AND (@branchId::uuid IS NULL OR g.branch_id = @branchId::uuid)
            GROUP BY a.id, a.code, a.name_ar
            ORDER BY a.code
            """, new { branchId }, cancellationToken: ct))).ToList();
    }

    /// <summary>
    /// تسوية حساب وسيط: قيد واحد يُفرغ الرصيد إلى خزينة/بنك (أو العكس إذا كان دائناً).
    /// </summary>
    public async Task<Guid> SettleClearingAsync(Guid treasuryId, Guid clearingAccountId,
        decimal amount, DateTime docDate, string? reference, Guid userId,
        CancellationToken ct = default)
    {
        amount = Math.Round(amount, 2, MidpointRounding.AwayFromZero);
        if (amount == 0)
            throw new ValidationAppException("validation.settlement_amount");

        await using var connection = await _factory.OpenAsync(ct);
        await using var tx = await connection.BeginTransactionAsync(ct);

        var treasury = await GetTreasuryRowAsync(connection, tx, treasuryId, ct)
            ?? throw new ValidationAppException("validation.treasury_not_found");

        var account = await connection.QuerySingleOrDefaultAsync<(Guid Id, string Code)>(
            new CommandDefinition(
                "SELECT id, code FROM accounts WHERE id = @clearingAccountId AND is_active " +
                "AND NOT is_group", new { clearingAccountId }, tx, cancellationToken: ct));
        if (account.Id == Guid.Empty)
            throw new ValidationAppException("validation.clearing_account_not_found");

        await connection.ExecuteAsync(new CommandDefinition(
            "SELECT fn_assert_period_open(@docDate::date, @branchId, 'ACCOUNTING')",
            new { docDate, branchId = treasury.BranchId }, tx, cancellationToken: ct));

        var value = Math.Abs(amount);
        var gl = amount > 0
            ? new List<JournalLine>
            {
                new(treasury.AccountId, value, 0),
                new(clearingAccountId, 0, value)
            }
            : new List<JournalLine>
            {
                new(clearingAccountId, value, 0),
                new(treasury.AccountId, 0, value)
            };

        var description = $"تسوية حساب وسيط {account.Code}" +
                          (string.IsNullOrWhiteSpace(reference) ? "" : $" — {reference.Trim()}");
        var entryId = Guid.CreateVersion7();
        await CreateVoucherEntryAsync(connection, tx, entryId, treasury.TreasuryType,
            treasury.BranchId, docDate, reference?.Trim() ?? account.Code, description, gl, userId,
            null, ct);

        await tx.CommitAsync(ct);
        return entryId;
    }

    /// <summary>مستندات الطرف المرحّلة التي لها رصيد غير مخصص (لشاشة التخصيص).</summary>
    public async Task<IReadOnlyList<OpenDocument>> ListOpenDocumentsAsync(string partyType,
        Guid partyId, int limit, CancellationToken ct = default)
    {
        await using var connection = await _factory.OpenAsync(ct);
        return (await connection.QueryAsync<OpenDocument>(new CommandDefinition("""
            SELECT d.id AS Id, d.doc_type AS DocType, d.doc_no AS DocNo, d.doc_date AS DocDate,
                   d.total AS Total, d.allocated_total AS AllocatedTotal
            FROM commercial_documents d
            WHERE d.status = 'posted'
              AND d.total > d.allocated_total
              AND ((@partyType = 'S' AND d.supplier_id = @partyId)
                OR (@partyType = 'C' AND d.customer_id = @partyId))
            ORDER BY d.doc_date, d.id
            LIMIT @limit
            """, new { partyType, partyId, limit = Math.Clamp(limit, 1, 500) },
            cancellationToken: ct))).ToList();
    }
}
