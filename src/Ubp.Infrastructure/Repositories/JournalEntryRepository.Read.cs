using Dapper;
using Ubp.Core.Models;
using Ubp.Infrastructure.Data;

namespace Ubp.Infrastructure.Repositories;

public sealed partial class JournalEntryRepository
{
    public async Task<JournalEntry?> GetAsync(Guid id, CancellationToken ct = default)
    {
        await using var connection = await _factory.OpenAsync(ct);
        return await connection.QuerySingleOrDefaultAsync<JournalEntry>(new CommandDefinition(
            $"SELECT {HeaderColumns} {HeaderFrom} WHERE e.id = @id", new { id }, cancellationToken: ct));
    }

    public async Task<JournalEntry?> GetBySourceAsync(string sourceType, Guid sourceId,
        CancellationToken ct = default)
    {
        await using var connection = await _factory.OpenAsync(ct);
        return await connection.QuerySingleOrDefaultAsync<JournalEntry>(new CommandDefinition(
            $"SELECT {HeaderColumns} {HeaderFrom} WHERE e.source_type = @sourceType " +
            "AND e.source_id = @sourceId AND e.reversal_of_id IS NULL " +
            "AND e.status <> 'cancelled' ORDER BY e.created_at DESC LIMIT 1",
            new { sourceType, sourceId }, cancellationToken: ct));
    }

    public async Task<IReadOnlyList<JournalEntry>> ListBySourceAsync(string sourceType,
        Guid branchId, CancellationToken ct = default)
    {
        await using var connection = await _factory.OpenAsync(ct);
        var rows = await connection.QueryAsync<JournalEntry>(new CommandDefinition(
            $"SELECT {HeaderColumns} {HeaderFrom} WHERE e.source_type = @sourceType " +
            "AND e.branch_id = @branchId ORDER BY e.entry_date DESC, e.id DESC",
            new { sourceType, branchId }, cancellationToken: ct));
        return rows.ToList();
    }

    public async Task<JournalEntryDetail?> GetDetailAsync(Guid id, CancellationToken ct = default)
    {
        await using var connection = await _factory.OpenAsync(ct);
        using var multi = await connection.QueryMultipleAsync(new CommandDefinition(
            $"SELECT {HeaderColumns} {HeaderFrom} WHERE e.id = @id;" +
            $"SELECT {LineColumns} {LineFrom} WHERE l.entry_id = @id ORDER BY l.line_no;",
            new { id }, cancellationToken: ct));

        var header = await multi.ReadSingleOrDefaultAsync<JournalEntry>();
        if (header is null) return null;
        var lines = (await multi.ReadAsync<JournalEntryLine>()).ToList();
        return new JournalEntryDetail { Header = header, Lines = lines };
    }
}
