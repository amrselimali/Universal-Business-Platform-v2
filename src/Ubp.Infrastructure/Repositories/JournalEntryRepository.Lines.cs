using Dapper;
using Npgsql;
using Ubp.Core.Models;

namespace Ubp.Infrastructure.Repositories;

public sealed partial class JournalEntryRepository
{
    /// <summary>ترقيم البنود بالترتيب وإدخالها داخل معاملة الحفظ.</summary>
    private static async Task InsertLinesAsync(NpgsqlConnection connection, NpgsqlTransaction tx,
        Guid entryId, IReadOnlyList<JournalEntryLineInput> lines, Guid branchId, DateTime entryDate,
        CancellationToken ct)
    {
        var lineNo = 0;
        foreach (var line in lines)
        {
            lineNo++;
            await connection.ExecuteAsync(new CommandDefinition("""
                INSERT INTO journal_entry_lines
                    (id, entry_id, line_no, account_id, description, debit, credit,
                     customer_id, supplier_id, cost_center_id, branch_id, entry_date)
                VALUES (@id, @entryId, @lineNo, @AccountId, @Description, @Debit, @Credit,
                        @CustomerId, @SupplierId, @CostCenterId, @branchId, @entryDate)
                """, new
            {
                id = Guid.CreateVersion7(), entryId, lineNo, line.AccountId, line.Description,
                line.Debit, line.Credit, line.CustomerId, line.SupplierId, line.CostCenterId,
                branchId, entryDate
            }, tx, cancellationToken: ct));
        }
    }

    /// <summary>إعادة حساب إجماليات الرأس من البنود (مصدر الحقيقة هو البنود).</summary>
    private static async Task RefreshTotalsAsync(NpgsqlConnection connection, NpgsqlTransaction tx,
        Guid entryId, CancellationToken ct)
    {
        await connection.ExecuteAsync(new CommandDefinition("""
            UPDATE journal_entries e
               SET total_debit = t.debit, total_credit = t.credit
              FROM (SELECT coalesce(sum(debit), 0) AS debit, coalesce(sum(credit), 0) AS credit
                      FROM journal_entry_lines WHERE entry_id = @entryId) t
             WHERE e.id = @entryId
            """, new { entryId }, tx, cancellationToken: ct));
    }
}
