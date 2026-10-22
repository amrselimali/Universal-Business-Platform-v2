using Dapper;
using Ubp.Core.Models;
using Ubp.Infrastructure.Data;

namespace Ubp.Infrastructure.Repositories;

public sealed partial class JournalRepository
{
    public async Task<Journal?> GetAsync(Guid id, CancellationToken ct = default)
    {
        await using var connection = await _factory.OpenAsync(ct);
        return await connection.QuerySingleOrDefaultAsync<Journal>(new CommandDefinition(
            $"SELECT {Columns} {FromSql} WHERE j.id = @id", new { id }, cancellationToken: ct));
    }

    public async Task<Journal?> GetByCodeAsync(string code, CancellationToken ct = default)
    {
        await using var connection = await _factory.OpenAsync(ct);
        return await connection.QuerySingleOrDefaultAsync<Journal>(new CommandDefinition(
            $"SELECT {Columns} {FromSql} WHERE j.code = @code", new { code }, cancellationToken: ct));
    }

    public async Task<Journal?> GetByTypeAsync(string journalType, CancellationToken ct = default)
    {
        await using var connection = await _factory.OpenAsync(ct);
        return await connection.QuerySingleOrDefaultAsync<Journal>(new CommandDefinition(
            $"SELECT {Columns} {FromSql} WHERE j.journal_type = @journalType AND j.is_active " +
            "ORDER BY j.sort_order LIMIT 1", new { journalType }, cancellationToken: ct));
    }

    public async Task<Guid> CreateAsync(Guid id, JournalInput input, Guid userId,
        CancellationToken ct = default)
    {
        await using var connection = await _factory.OpenAsync(ct);
        return await connection.ExecuteScalarAsync<Guid>(new CommandDefinition("""
            INSERT INTO journals (id, code, name_ar, name_en, journal_type, default_account_id,
                                  allow_manual, sort_order, is_active, created_by)
            VALUES (@id, @Code, @NameAr, @NameEn, @JournalType, @DefaultAccountId,
                    @AllowManual, @SortOrder, @IsActive, @userId)
            RETURNING id
            """, new
        {
            id, input.Code, input.NameAr, input.NameEn, input.JournalType, input.DefaultAccountId,
            input.AllowManual, input.SortOrder, input.IsActive, userId
        }, cancellationToken: ct));
    }

    public async Task<Journal?> UpdateAsync(Guid id, int version, JournalInput input, Guid userId,
        CancellationToken ct = default)
    {
        await using var connection = await _factory.OpenAsync(ct);
        var updated = await connection.ExecuteScalarAsync<Guid?>(new CommandDefinition("""
            UPDATE journals SET code = @Code, name_ar = @NameAr, name_en = @NameEn,
                                journal_type = @JournalType, default_account_id = @DefaultAccountId,
                                allow_manual = @AllowManual, sort_order = @SortOrder,
                                is_active = @IsActive, updated_by = @userId
            WHERE id = @id AND version = @version
            RETURNING id
            """, new
        {
            id, version, input.Code, input.NameAr, input.NameEn, input.JournalType,
            input.DefaultAccountId, input.AllowManual, input.SortOrder, input.IsActive, userId
        }, cancellationToken: ct));
        return updated is null ? null : await GetAsync(id, ct);
    }

    public async Task<Journal?> SetActiveAsync(Guid id, bool isActive, Guid userId,
        CancellationToken ct = default)
    {
        await using var connection = await _factory.OpenAsync(ct);
        var rows = await connection.ExecuteAsync(new CommandDefinition(
            "UPDATE journals SET is_active = @isActive, updated_by = @userId WHERE id = @id",
            new { id, isActive, userId }, cancellationToken: ct));
        return rows == 0 ? null : await GetAsync(id, ct);
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        await using var connection = await _factory.OpenAsync(ct);
        return await connection.ExecuteAsync(new CommandDefinition(
            "DELETE FROM journals WHERE id = @id", new { id }, cancellationToken: ct)) > 0;
    }
}
