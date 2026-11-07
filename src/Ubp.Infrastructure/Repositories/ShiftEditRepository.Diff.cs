using Dapper;
using Npgsql;
using Ubp.Core.Models;
using Ubp.Core.Results;

namespace Ubp.Infrastructure.Repositories;

public sealed partial class ShiftEditRepository
{
    private static async Task<Guid> NeedAccAsync(NpgsqlConnection connection, NpgsqlTransaction tx,
        string key, CancellationToken ct)
    {
        var id = await connection.ExecuteScalarAsync<Guid?>(new CommandDefinition(
            "SELECT account_id FROM posting_defaults WHERE setting_key = @k", new { k = key }, tx,
            cancellationToken: ct));
        return id ?? throw new ValidationAppException("validation.posting_account_missing",
            new Dictionary<string, string> { ["key"] = key, ["module"] = "shift_edit" });
    }

    /// <summary>
    /// قيد فرق (موجب/سالب) لتعديل مستند مُرحَّل: مدين طرف ودائن آخر بقيمة الفرق فقط — source review_adjustment.
    /// يُحدَّد الطرف (عميل/مورد) على السطر المطابق لحسابه (حساب الذمم/الدفعات) لا على الطرف الآخر.
    /// </summary>
    private static async Task PostDifferenceAsync(NpgsqlConnection connection,
        NpgsqlTransaction tx, Guid branchId, string docNo, Guid debitAccount, Guid? debitCustomer,
        Guid? debitSupplier, Guid creditAccount, Guid? creditCustomer, Guid? creditSupplier,
        decimal amount, Guid sourceId, Guid userId, CancellationToken ct)
    {
        if (amount == 0 || debitAccount == creditAccount) return;
        var journalId = await connection.ExecuteScalarAsync<Guid?>(new CommandDefinition(
            "SELECT id FROM journals WHERE journal_type = 'sales' AND is_active " +
            "ORDER BY sort_order LIMIT 1", transaction: tx, cancellationToken: ct))
            ?? throw new ValidationAppException("validation.journal_not_found");

        var entryId = Guid.CreateVersion7();
        var description = $"فرق تعديل وردية {docNo}";
        await connection.ExecuteAsync(new CommandDefinition("""
            INSERT INTO journal_entries
                (id, journal_id, branch_id, entry_date, reference, description, status,
                 source_type, source_id, total_debit, total_credit, created_by)
            VALUES (@entryId, @journalId, @branchId, current_date, @docNo, @description, 'draft',
                    'review_adjustment', @sourceId, @amount, @amount, @userId)
            """, new { entryId, journalId, branchId, docNo, description, sourceId, amount, userId },
            tx, cancellationToken: ct));

        await connection.ExecuteAsync(new CommandDefinition("""
            INSERT INTO journal_entry_lines
                (id, entry_id, line_no, account_id, description, debit, credit, customer_id,
                 supplier_id, branch_id, entry_date)
            VALUES (@id1, @entryId, 1, @debit, @description, @amount, 0, @debitCustomer,
                    @debitSupplier, @branchId, current_date),
                   (@id2, @entryId, 2, @credit, @description, 0, @amount, @creditCustomer,
                    @creditSupplier, @branchId, current_date)
            """, new { id1 = Guid.CreateVersion7(), id2 = Guid.CreateVersion7(), entryId,
                debit = debitAccount, credit = creditAccount, description, amount,
                debitCustomer, debitSupplier, creditCustomer, creditSupplier, branchId }, tx,
            cancellationToken: ct));

        await connection.ExecuteAsync(new CommandDefinition(
            "SELECT fn_gl_post_entry(@entryId, @userId)", new { entryId, userId }, tx,
            cancellationToken: ct));
    }
}
