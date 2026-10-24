using Dapper;
using Npgsql;
using Ubp.Core.Models;
using Ubp.Core.Results;

namespace Ubp.Infrastructure.Repositories;

public sealed partial class SalesFinanceRepository
{
    private static async Task<Dictionary<string, Guid>> LoadDefaultsAsync(NpgsqlConnection c,
        NpgsqlTransaction tx, CancellationToken ct) =>
        (await c.QueryAsync<(string Key, Guid? AccountId)>(new CommandDefinition(
            "SELECT setting_key, account_id FROM posting_defaults", transaction: tx,
            cancellationToken: ct)))
        .Where(r => r.AccountId.HasValue)
        .ToDictionary(r => r.Key, r => r.AccountId!.Value, StringComparer.OrdinalIgnoreCase);

    private static Guid Need(Dictionary<string, Guid> defaults, string key, string module) =>
        defaults.TryGetValue(key, out var account) ? account
            : throw new ValidationAppException("validation.posting_account_missing",
                new Dictionary<string, string> { ["key"] = key, ["module"] = module });

    private static async Task<Guid> InsertEntryAsync(NpgsqlConnection connection,
        NpgsqlTransaction tx, string journalType, Guid branchId, DateTime docDate, string docNo,
        string description, IReadOnlyList<EntryLine> gl, Guid userId, string sourceType,
        Guid? sourceId, CancellationToken ct)
    {
        var debit = gl.Sum(x => x.Debit);
        var credit = gl.Sum(x => x.Credit);
        if (Math.Round(debit - credit, 2, MidpointRounding.AwayFromZero) != 0)
            throw new ValidationAppException("validation.entry_not_balanced");

        var journalId = await connection.ExecuteScalarAsync<Guid?>(new CommandDefinition(
            "SELECT id FROM journals WHERE journal_type = @journalType AND is_active " +
            "ORDER BY sort_order LIMIT 1", new { journalType }, tx, cancellationToken: ct))
            ?? throw new ValidationAppException("validation.journal_not_found");

        var entryId = Guid.CreateVersion7();
        await connection.ExecuteAsync(new CommandDefinition("""
            INSERT INTO journal_entries (id, journal_id, branch_id, entry_date, reference,
                description, status, source_type, source_id, total_debit, total_credit, created_by)
            VALUES (@entryId, @journalId, @branchId, @docDate, @docNo, @description, 'draft',
                    @sourceType, @sourceId, @debit, @credit, @userId)
            """, new { entryId, journalId, branchId, docDate, docNo, description, sourceType,
                sourceId, debit, credit, userId }, tx, cancellationToken: ct));

        var lineNo = 0;
        foreach (var line in gl)
        {
            lineNo++;
            await connection.ExecuteAsync(new CommandDefinition("""
                INSERT INTO journal_entry_lines (id, entry_id, line_no, account_id, description,
                    debit, credit, customer_id, branch_id, entry_date)
                VALUES (@lineId, @entryId, @lineNo, @accountId, @description, @debit, @credit,
                        @customerId, @branchId, @docDate)
                """, new
            {
                lineId = Guid.CreateVersion7(), entryId, lineNo, accountId = line.AccountId,
                description, debit = line.Debit, credit = line.Credit,
                customerId = line.CustomerId, branchId, docDate
            }, tx, cancellationToken: ct));
        }

        await connection.ExecuteAsync(new CommandDefinition(
            "SELECT fn_gl_post_entry(@entryId, @userId)", new { entryId, userId }, tx,
            cancellationToken: ct));
        return entryId;
    }
}
