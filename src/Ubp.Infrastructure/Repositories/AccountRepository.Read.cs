using Dapper;
using Ubp.Core.Models;
using Ubp.Infrastructure.Data;

namespace Ubp.Infrastructure.Repositories;

public sealed partial class AccountRepository
{
    public async Task<IReadOnlyList<Account>> ListAllAsync(bool includeInactive, CancellationToken ct = default)
    {
        var where = includeInactive ? "WHERE true" : "WHERE a.is_active";
        await using var connection = await _factory.OpenAsync(ct);
        var rows = await connection.QueryAsync<Account>(new CommandDefinition(
            $"SELECT {Columns} FROM accounts a {where} ORDER BY a.path", cancellationToken: ct));
        return rows.ToList();
    }

    public async Task<IReadOnlyList<Account>> ListPostableAsync(CancellationToken ct = default)
    {
        await using var connection = await _factory.OpenAsync(ct);
        var rows = await connection.QueryAsync<Account>(new CommandDefinition(
            $"SELECT {Columns} FROM accounts a WHERE a.is_active AND NOT a.is_group ORDER BY a.code",
            cancellationToken: ct));
        return rows.ToList();
    }

    public async Task<Account?> GetAsync(Guid id, CancellationToken ct = default)
    {
        await using var connection = await _factory.OpenAsync(ct);
        return await connection.QuerySingleOrDefaultAsync<Account>(new CommandDefinition(
            $"SELECT {Columns} FROM accounts a WHERE a.id = @id", new { id }, cancellationToken: ct));
    }

    public async Task<Account?> GetByCodeAsync(string code, CancellationToken ct = default)
    {
        await using var connection = await _factory.OpenAsync(ct);
        return await connection.QuerySingleOrDefaultAsync<Account>(new CommandDefinition(
            $"SELECT {Columns} FROM accounts a WHERE a.code = @code", new { code }, cancellationToken: ct));
    }

    public async Task<Account?> FindByTypeAsync(string accountType, CancellationToken ct = default)
    {
        await using var connection = await _factory.OpenAsync(ct);
        return await connection.QuerySingleOrDefaultAsync<Account>(new CommandDefinition(
            $"SELECT {Columns} FROM accounts a WHERE a.account_type = @accountType AND a.is_active " +
            "AND NOT a.is_group ORDER BY a.code LIMIT 1", new { accountType }, cancellationToken: ct));
    }

    public async Task<int> CountUsageAsync(Guid id, CancellationToken ct = default)
    {
        const string sql = """
            SELECT (SELECT count(*)::int FROM journal_entry_lines WHERE account_id = @id)
                 + (SELECT count(*)::int FROM accounts WHERE parent_id = @id)
                 + (SELECT count(*)::int FROM posting_defaults WHERE account_id = @id)
                 + (SELECT count(*)::int FROM item_category_accounts
                    WHERE inventory_account_id = @id OR cogs_account_id = @id
                       OR adjustment_gain_account_id = @id OR adjustment_loss_account_id = @id)
            """;
        await using var connection = await _factory.OpenAsync(ct);
        return await connection.ExecuteScalarAsync<int>(new CommandDefinition(sql, new { id },
            cancellationToken: ct));
    }

    public async Task<int> CountAsync(CancellationToken ct = default)
    {
        await using var connection = await _factory.OpenAsync(ct);
        return await connection.ExecuteScalarAsync<int>(new CommandDefinition(
            "SELECT count(*)::int FROM accounts", cancellationToken: ct));
    }
}
