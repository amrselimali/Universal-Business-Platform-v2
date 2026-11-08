using Dapper;
using Npgsql;
using Ubp.Core.Models;
using Ubp.Core.Results;

namespace Ubp.Infrastructure.Repositories;

public sealed partial class OperatingExpenseRepository
{
    /// <summary>قيد المصروف: مدين كل سطر على حسابه المختار، ودائن حساب المورد بالقيمة الكلية.</summary>
    private static async Task<Guid> PostExpenseEntryAsync(NpgsqlConnection connection,
        NpgsqlTransaction tx, PostHeader head, IReadOnlyList<PostLine> lines, decimal total,
        string docNo, Guid userId, CancellationToken ct)
    {
        var payable = await NeedAsync(connection, tx, PostingKeys.SupplierPayable, ct);
        var gl = lines
            .Select(l => new EntryLine(l.AccountId, l.Amount, 0, null, l.CostCenterId))
            .ToList();
        gl.Add(new EntryLine(payable, 0, total, head.SupplierId));

        return await PostEntryAsync(connection, tx, "purchase", head.BranchId, head.DocDate,
            docNo, $"مصروفات تشغيلية {docNo}", gl, userId, "operating_expense", head.Id, ct);
    }

    /// <summary>سداد فوري: سند صرف مرحّل (للخزينة والشفت) + قيد مدين المورد / دائن الخزينة.</summary>
    private static async Task<Guid> PostPaymentAsync(NpgsqlConnection connection,
        NpgsqlTransaction tx, PostHeader head, decimal total, string docNo, Guid userId,
        CancellationToken ct)
    {
        // الخزائن هي طرق الدفع: تُستخدم الطريقة الافتراضية للخزينة تلقائياً — 0093.
        var methodId = head.TreasuryMethodId
                       ?? await DefaultMethodIdAsync(connection, tx, head.TreasuryId!.Value, ct)
                       ?? throw new ValidationAppException("validation.treasury_method_not_found");

        var method = await connection.QuerySingleAsync<(Guid AccountId, Guid? ClearingAccountId,
            string TreasuryType)>(new CommandDefinition("""
            SELECT t.account_id AS AccountId, m.clearing_account_id AS ClearingAccountId,
                   t.treasury_type AS TreasuryType
            FROM treasury_methods m JOIN treasuries t ON t.id = m.treasury_id
            WHERE m.id = @methodId
            """, new { methodId }, tx, cancellationToken: ct));

        var partyName = await connection.ExecuteScalarAsync<string>(new CommandDefinition(
            "SELECT name_ar FROM suppliers WHERE id = @id", new { id = head.SupplierId }, tx,
            cancellationToken: ct));

        var cashAccount = method.ClearingAccountId ?? method.AccountId;
        var payable = await NeedAsync(connection, tx, PostingKeys.SupplierPayable, ct);
        var voucherId = Guid.CreateVersion7();
        var voucherNo = await connection.ExecuteScalarAsync<string>(new CommandDefinition("""
            SELECT next_document_number('PAY'::varchar, @branchId::uuid, @date::date,
                                        'PAY'::varchar, 6::smallint)
            """, new { branchId = head.BranchId, date = head.DocDate.Date }, tx,
            cancellationToken: ct));

        await connection.ExecuteAsync(new CommandDefinition("""
            INSERT INTO vouchers
                (id, branch_id, doc_date, status, voucher_type, party_type, party_id, party_name,
                 treasury_id, total_amount, fee_total, notes, created_by)
            VALUES (@id, @branchId, @docDate, 'draft', 'payment', 'S', @supplierId, @partyName,
                    @treasuryId, @total, 0, @notes, @userId)
            """, new
        {
            id = voucherId, branchId = head.BranchId, docDate = head.DocDate.Date,
            supplierId = head.SupplierId, partyName, treasuryId = head.TreasuryId, total,
            notes = $"سداد مصروف تشغيلي {docNo}", userId
        }, tx, cancellationToken: ct));

        await connection.ExecuteAsync(new CommandDefinition("""
            INSERT INTO voucher_lines (voucher_id, line_no, treasury_method_id, amount, fee_amount)
            VALUES (@voucherId, 1, @methodId, @amount, 0)
            """, new { voucherId, methodId, amount = total }, tx,
            cancellationToken: ct));

        var gl = new List<EntryLine>
        {
            new(payable, total, 0, head.SupplierId),
            new(cashAccount, 0, total)
        };
        var journalType = method.TreasuryType == TreasuryTypes.Cash ? "cash" : "bank";
        var entryId = await PostEntryAsync(connection, tx, journalType, head.BranchId,
            head.DocDate, voucherNo, $"سداد مصروف تشغيلي {docNo}", gl, userId, "voucher",
            voucherId, ct);

        await connection.ExecuteAsync(new CommandDefinition("""
            UPDATE vouchers SET status = 'posted', doc_no = @voucherNo, posted_at = now(),
                posted_by = @userId, journal_entry_id = @entryId, updated_by = @userId
            WHERE id = @voucherId
            """, new { voucherNo, userId, entryId, voucherId }, tx, cancellationToken: ct));

        return voucherId;
    }
}
