using Dapper;
using Npgsql;
using Ubp.Core.Results;

namespace Ubp.Infrastructure.Repositories;

public sealed partial class OperatingExpenseRepository
{
    private sealed record EntryLine(Guid AccountId, decimal Debit, decimal Credit,
        Guid? SupplierId = null, Guid? CostCenterId = null);

    private static async Task<Guid> NeedAsync(NpgsqlConnection connection, NpgsqlTransaction tx,
        string key, CancellationToken ct)
    {
        var account = await connection.ExecuteScalarAsync<Guid?>(new CommandDefinition(
            "SELECT account_id FROM posting_defaults WHERE setting_key = @key", new { key }, tx,
            cancellationToken: ct));
        return account ?? throw new ValidationAppException("validation.posting_account_missing",
            new Dictionary<string, string> { ["key"] = key, ["module"] = "purchasing" });
    }

    /// <summary>قيد أستاذ داخل المعاملة: رأس + سطور + ترحيل ذري عبر fn_gl_post_entry.</summary>
    private static async Task<Guid> PostEntryAsync(NpgsqlConnection connection,
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
            """, new
        {
            entryId, journalId, branchId, docDate = docDate.Date, docNo, description, sourceType,
            sourceId, debit, credit, userId
        }, tx, cancellationToken: ct));

        var lineNo = 0;
        foreach (var line in gl)
        {
            lineNo++;
            await connection.ExecuteAsync(new CommandDefinition("""
                INSERT INTO journal_entry_lines (id, entry_id, line_no, account_id, description,
                    debit, credit, supplier_id, cost_center_id, branch_id, entry_date)
                VALUES (@lineId, @entryId, @lineNo, @accountId, @description, @debit, @credit,
                        @supplierId, @costCenterId, @branchId, @docDate)
                """, new
            {
                lineId = Guid.CreateVersion7(), entryId, lineNo, accountId = line.AccountId,
                description, debit = line.Debit, credit = line.Credit,
                supplierId = line.SupplierId, costCenterId = line.CostCenterId, branchId,
                docDate = docDate.Date
            }, tx, cancellationToken: ct));
        }

        await connection.ExecuteAsync(new CommandDefinition(
            "SELECT fn_gl_post_entry(@entryId, @userId)", new { entryId, userId }, tx,
            cancellationToken: ct));
        return entryId;
    }
}
