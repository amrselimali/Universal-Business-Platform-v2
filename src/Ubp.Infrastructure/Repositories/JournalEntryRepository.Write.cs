using Dapper;
using Npgsql;
using Ubp.Core.Models;
using Ubp.Infrastructure.Data;

namespace Ubp.Infrastructure.Repositories;

public sealed partial class JournalEntryRepository
{
    public async Task<Guid> CreateDraftAsync(Guid id, JournalEntryInput input, Guid userId,
        string sourceType = EntrySources.Manual, Guid? sourceId = null,
        CancellationToken ct = default)
    {
        await using var connection = await _factory.OpenAsync(ct);
        await using var tx = await connection.BeginTransactionAsync(ct);

        await connection.ExecuteAsync(new CommandDefinition("""
            INSERT INTO journal_entries
                (id, journal_id, branch_id, entry_date, reference, description, status,
                 source_type, source_id, created_by)
            VALUES (@id, @JournalId, @BranchId, @EntryDate, @Reference, @Description, 'draft',
                    @sourceType, @sourceId, @userId)
            """, new
        {
            id, input.JournalId, input.BranchId, input.EntryDate, input.Reference,
            input.Description, sourceType, sourceId, userId
        }, tx, cancellationToken: ct));

        await InsertLinesAsync(connection, tx, id, input.Lines, input.BranchId, input.EntryDate, ct);
        await RefreshTotalsAsync(connection, tx, id, ct);

        await tx.CommitAsync(ct);
        return id;
    }

    public async Task<JournalEntry?> UpdateDraftAsync(Guid id, int version, JournalEntryInput input,
        Guid userId, CancellationToken ct = default)
    {
        await using var connection = await _factory.OpenAsync(ct);
        await using var tx = await connection.BeginTransactionAsync(ct);

        var status = await connection.ExecuteScalarAsync<string?>(new CommandDefinition(
            "SELECT status FROM journal_entries WHERE id = @id AND version = @version FOR UPDATE",
            new { id, version }, tx, cancellationToken: ct));
        if (status is null || status != EntryStatuses.Draft)
        {
            await tx.RollbackAsync(ct);
            return null;
        }

        await connection.ExecuteAsync(new CommandDefinition("""
            UPDATE journal_entries
               SET journal_id = @JournalId, branch_id = @BranchId, entry_date = @EntryDate,
                   reference = @Reference, description = @Description, updated_by = @userId
             WHERE id = @id AND version = @version
            """, new
        {
            id, version, input.JournalId, input.BranchId, input.EntryDate, input.Reference,
            input.Description, userId
        }, tx, cancellationToken: ct));

        await connection.ExecuteAsync(new CommandDefinition(
            "DELETE FROM journal_entry_lines WHERE entry_id = @id", new { id }, tx,
            cancellationToken: ct));

        await InsertLinesAsync(connection, tx, id, input.Lines, input.BranchId, input.EntryDate, ct);
        await RefreshTotalsAsync(connection, tx, id, ct);

        await tx.CommitAsync(ct);
        return await GetAsync(id, ct);
    }

    public async Task<JournalEntry?> CancelAsync(Guid id, Guid userId, CancellationToken ct = default)
    {
        await using var connection = await _factory.OpenAsync(ct);
        var rows = await connection.ExecuteAsync(new CommandDefinition("""
            UPDATE journal_entries SET status = 'cancelled', updated_by = @userId
            WHERE id = @id AND status = 'draft'
            """, new { id, userId }, cancellationToken: ct));
        return rows == 0 ? null : await GetAsync(id, ct);
    }

    public async Task<bool> DeleteDraftAsync(Guid id, CancellationToken ct = default)
    {
        await using var connection = await _factory.OpenAsync(ct);
        return await connection.ExecuteAsync(new CommandDefinition(
            "DELETE FROM journal_entries WHERE id = @id AND status = 'draft'",
            new { id }, cancellationToken: ct)) > 0;
    }
}
