using Dapper;
using Npgsql;
using Ubp.Core.Models;
using Ubp.Core.Results;

namespace Ubp.Infrastructure.Repositories;

public sealed partial class TreasuryRepository
{
    /// <summary>
    /// تسليم نقدية من خزينة إلى حساب وسيط لنقدية الفرع (0080): يخفض الخزينة ويزيد الوسيط
    /// بقيد واحد يظهر ضمن مصروفات الشفت.
    /// </summary>
    public async Task<Guid> CreateCashHandoverAsync(Guid id, CashHandoverInput input,
        Guid userId, CancellationToken ct = default)
    {
        input.Amount = Math.Round(input.Amount, 2, MidpointRounding.AwayFromZero);
        input.CommissionAmount = Math.Round(input.CommissionAmount, 2,
            MidpointRounding.AwayFromZero);
        if (input.CommissionAmount < 0)
            throw new ValidationAppException("validation.payment_fee");
        if (input.Amount <= 0)
            throw new ValidationAppException("validation.voucher_amount");
        if (input.TreasuryId == Guid.Empty)
            throw new ValidationAppException("validation.required_fields");

        await using var connection = await _factory.OpenAsync(ct);
        await using var tx = await connection.BeginTransactionAsync(ct);

        var treasury = await connection.QuerySingleOrDefaultAsync<(Guid AccountId, Guid BranchId,
            string TreasuryType, bool FollowInShift)>(new CommandDefinition("""
            SELECT account_id AS AccountId, branch_id AS BranchId,
                   treasury_type AS TreasuryType, follow_in_shift AS FollowInShift
            FROM treasuries WHERE id = @treasuryId AND is_active
            """, new { treasuryId = input.TreasuryId }, tx, cancellationToken: ct));
        if (treasury.AccountId == Guid.Empty)
            throw new ValidationAppException("validation.treasury_not_found");
        if (treasury.BranchId != input.BranchId)
            throw new ValidationAppException("validation.treasury_branch_mismatch");

        // الحساب الوسيط لنقدية الفرع: يُحدَّد افتراضياً من شاشة إعدادات الحسابات — 0093.
        var fromDefault = false;
        if (input.ClearingAccountId == Guid.Empty)
        {
            var fallback = await connection.ExecuteScalarAsync<Guid?>(new CommandDefinition("""
                SELECT COALESCE(
                    (SELECT account_id FROM posting_defaults WHERE setting_key = @key),
                    (SELECT id FROM accounts WHERE is_cash_clearing AND is_active AND NOT is_group
                     ORDER BY code LIMIT 1))
                """, new { key = PostingKeys.CashClearing }, tx, cancellationToken: ct));
            if (fallback is null || fallback == Guid.Empty)
                throw new ValidationAppException("validation.posting_account_missing",
                    new Dictionary<string, string>
                    {
                        ["key"] = PostingKeys.CashClearing, ["module"] = "treasury"
                    });
            input.ClearingAccountId = fallback.Value;
            fromDefault = true;
        }

        // الحساب المختار صراحةً يجب أن يكون مكوَّداً «وسيطاً لنقدية الفرع»، أما الافتراضي
        // القادم من إعدادات الحسابات فيكفي أن يكون حساباً قابلاً للترحيل.
        var clearingOk = fromDefault
            ? await connection.ExecuteScalarAsync<bool>(new CommandDefinition(
                "SELECT EXISTS (SELECT 1 FROM accounts WHERE id = @id AND is_active " +
                "AND NOT is_group)", new { id = input.ClearingAccountId }, tx,
                cancellationToken: ct))
            : await connection.ExecuteScalarAsync<bool>(new CommandDefinition(
                "SELECT EXISTS (SELECT 1 FROM accounts WHERE id = @id AND is_cash_clearing " +
                "AND is_active AND NOT is_group)",
                new { id = input.ClearingAccountId }, tx, cancellationToken: ct));
        if (!clearingOk)
            throw new ValidationAppException("validation.cash_clearing_account_required");

        if (input.CommissionAmount > 0 && !treasury.FollowInShift)
            throw new ValidationAppException("validation.handover_commission_requires_shift");

        var gl = new List<JournalLine>
        {
            new(input.ClearingAccountId, input.Amount, 0),
            new(treasury.AccountId, 0, input.Amount)
        };

        // عمولة التسليم: تُحمَّل على حساب عمولة الخزينة (أو الحساب الافتراضي من إعدادات
        // الحسابات) وتُضاف على الخزينة (خصماً) — 0082/0093.
        if (input.CommissionAmount > 0)
        {
            var feeAccount = await connection.ExecuteScalarAsync<Guid?>(new CommandDefinition("""
                SELECT COALESCE(
                    (SELECT provider_fee_account_id FROM treasuries WHERE id = @id),
                    (SELECT account_id FROM posting_defaults
                     WHERE setting_key = @key AND account_id IS NOT NULL),
                    (SELECT account_id FROM posting_defaults
                     WHERE setting_key = @feesKey AND account_id IS NOT NULL))
                """, new { id = input.TreasuryId, key = PostingKeys.CashHandoverCommission,
                    feesKey = PostingKeys.PaymentFees }, tx, cancellationToken: ct));
            if (feeAccount is null)
                throw new ValidationAppException("validation.posting_account_missing",
                    new Dictionary<string, string>
                    {
                        ["key"] = PostingKeys.CashHandoverCommission, ["module"] = "treasury"
                    });
            gl.Add(new JournalLine(feeAccount.Value, input.CommissionAmount, 0));
            gl.Add(new JournalLine(treasury.AccountId, 0, input.CommissionAmount));
        }

        await connection.ExecuteAsync(new CommandDefinition(
            "SELECT fn_assert_period_open(@docDate::date, @branchId, 'ACCOUNTING')",
            new { docDate = input.DocDate.Date, input.BranchId }, tx, cancellationToken: ct));

        await CreateVoucherEntryAsync(connection, tx, Guid.CreateVersion7(),
            treasury.TreasuryType, input.BranchId, input.DocDate.Date, $"CH-{id:N}"[..12],
            "تسليم نقدية إلى حساب وسيط", gl, userId, id, ct, "cash_handover",
            treasury.TreasuryType == TreasuryTypes.Cash ? "cash" : "bank");

        var entryId = await connection.ExecuteScalarAsync<Guid?>(new CommandDefinition(
            "SELECT id FROM journal_entries WHERE source_type = 'cash_handover' AND source_id = @id",
            new { id }, tx, cancellationToken: ct));

        await connection.ExecuteAsync(new CommandDefinition("""
            INSERT INTO cash_handovers (id, branch_id, treasury_id, clearing_account_id, amount,
                commission_amount, doc_date, notes, journal_entry_id, created_by)
            VALUES (@id, @BranchId, @TreasuryId, @ClearingAccountId, @Amount, @CommissionAmount,
                    @docDate, @Notes, @entryId, @userId)
            """, new { id, input.BranchId, input.TreasuryId, input.ClearingAccountId, input.Amount,
                input.CommissionAmount, docDate = input.DocDate.Date, input.Notes, entryId,
                userId }, tx,
            cancellationToken: ct));

        await tx.CommitAsync(ct);
        return id;
    }

    public async Task<IReadOnlyList<CashHandover>> ListCashHandoversAsync(Guid branchId,
        DateTime? from, DateTime? to, int limit, CancellationToken ct = default)
    {
        await using var connection = await _factory.OpenAsync(ct);
        return (await connection.QueryAsync<CashHandover>(new CommandDefinition("""
            SELECT h.id AS Id, h.branch_id AS BranchId, h.treasury_id AS TreasuryId,
                   t.code AS TreasuryCode, t.name_ar AS TreasuryName,
                   t.treasury_type AS TreasuryType, h.clearing_account_id AS ClearingAccountId,
                   a.code AS AccountCode, a.name_ar AS AccountName, h.amount AS Amount,
                   h.commission_amount AS CommissionAmount,
                   h.doc_date AS DocDate, h.notes AS Notes
            FROM cash_handovers h
            JOIN treasuries t ON t.id = h.treasury_id
            JOIN accounts a ON a.id = h.clearing_account_id
            WHERE h.branch_id = @branchId
              AND (@from::date IS NULL OR h.doc_date >= @from::date)
              AND (@to::date IS NULL OR h.doc_date <= @to::date)
            ORDER BY h.created_at DESC
            LIMIT @limit
            """, new { branchId, from = from?.Date, to = to?.Date,
                limit = Math.Clamp(limit, 1, 500) }, cancellationToken: ct))).ToList();
    }
}
