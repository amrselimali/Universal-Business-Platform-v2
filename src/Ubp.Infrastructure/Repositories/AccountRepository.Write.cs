using Dapper;
using Ubp.Core.Models;
using Ubp.Infrastructure.Data;

namespace Ubp.Infrastructure.Repositories;

public sealed partial class AccountRepository
{
    public async Task<Guid> CreateAsync(Guid id, AccountInput input, string path, short level,
        Guid userId, bool isSystem = false, CancellationToken ct = default)
    {
        const string sql = """
            INSERT INTO accounts (id, code, name_ar, name_en, parent_id, path, level, is_group,
                                  account_type, cost_center_policy, is_system, notes, is_active,
                                  created_by)
            VALUES (@id, @Code, @NameAr, @NameEn, @ParentId, @path, @level, @IsGroup,
                    @AccountType, @CostCenterPolicy, @isSystem, @Notes, @IsActive, @userId)
            RETURNING id
            """;
        await using var connection = await _factory.OpenAsync(ct);
        return await connection.ExecuteScalarAsync<Guid>(new CommandDefinition(sql, new
        {
            id, input.Code, input.NameAr, input.NameEn, input.ParentId, path, level, input.IsGroup,
            input.AccountType, input.CostCenterPolicy, isSystem, input.Notes, input.IsActive, userId
        }, cancellationToken: ct));
    }

    public async Task<Account?> UpdateAsync(Guid id, int version, AccountInput input, Guid userId,
        CancellationToken ct = default)
    {
        await using var connection = await _factory.OpenAsync(ct);
        await using var tx = await connection.BeginTransactionAsync(ct);

        var current = await connection.QuerySingleOrDefaultAsync<CurrentRow>(new CommandDefinition(
            "SELECT path AS Path, code AS Code FROM accounts " +
            "WHERE id = @id AND version = @version FOR UPDATE",
            new { id, version }, tx, cancellationToken: ct));
        if (current is null) return null;

        var newPath = current.Path;
        if (!string.Equals(current.Code, input.Code, StringComparison.Ordinal))
        {
            var prefix = current.Path[..(current.Path.Length - current.Code.Length - 1)];
            newPath = prefix + input.Code + "/";
            await MoveSubtreeAsync(connection, tx, id, current.Path, newPath, 0, ct);
        }

        var updated = await connection.ExecuteScalarAsync<Guid?>(new CommandDefinition("""
            UPDATE accounts SET code = @Code, name_ar = @NameAr, name_en = @NameEn,
                                is_group = @IsGroup, account_type = @AccountType,
                                cost_center_policy = @CostCenterPolicy, notes = @Notes,
                                is_active = @IsActive, path = @path, updated_by = @userId
            WHERE id = @id AND version = @version
            RETURNING id
            """, new
        {
            id, version, input.Code, input.NameAr, input.NameEn, input.IsGroup,
            input.AccountType, input.CostCenterPolicy, input.Notes, input.IsActive,
            path = newPath, userId
        }, tx, cancellationToken: ct));

        if (updated is null)
        {
            await tx.RollbackAsync(ct);
            return null;
        }

        await tx.CommitAsync(ct);
        return await GetAsync(id, ct);
    }

    public async Task<Account?> SetActiveAsync(Guid id, bool isActive, Guid userId,
        CancellationToken ct = default)
    {
        await using var connection = await _factory.OpenAsync(ct);
        var rows = await connection.ExecuteAsync(new CommandDefinition(
            "UPDATE accounts SET is_active = @isActive, updated_by = @userId WHERE id = @id",
            new { id, isActive, userId }, cancellationToken: ct));
        return rows == 0 ? null : await GetAsync(id, ct);
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        await using var connection = await _factory.OpenAsync(ct);
        return await connection.ExecuteAsync(new CommandDefinition(
            "DELETE FROM accounts WHERE id = @id", new { id }, cancellationToken: ct)) > 0;
    }

    private sealed class CurrentRow
    {
        public string Path { get; set; } = "/";
        public string Code { get; set; } = "";
    }
}
