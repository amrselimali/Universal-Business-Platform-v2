using Dapper;
using Ubp.Core.Models;
using Ubp.Infrastructure.Data;

namespace Ubp.Infrastructure.Repositories;

public interface IBranchAccountDefaultsRepository
{
    Task<IReadOnlyList<BranchAccountDefault>> ListAsync(Guid branchId, CancellationToken ct = default);
    Task<Guid> UpsertAsync(BranchAccountDefaultInput input, Guid userId, CancellationToken ct = default);
    Task<bool> DeleteAsync(Guid branchId, string key, CancellationToken ct = default);
    Task<Guid?> ResolveAsync(Guid branchId, string key, CancellationToken ct = default);
}

/// <summary>حسابات الفرع الافتراضية: قراءة/كتابة محدودة بجدول واحد، ويُستخدم <see cref="ResolveAsync"/> عند الترحيل.</summary>
public sealed class BranchAccountDefaultsRepository : IBranchAccountDefaultsRepository
{
    private readonly IDbConnectionFactory _factory;

    public BranchAccountDefaultsRepository(IDbConnectionFactory factory) => _factory = factory;

    public async Task<IReadOnlyList<BranchAccountDefault>> ListAsync(Guid branchId,
        CancellationToken ct = default)
    {
        await using var connection = await _factory.OpenAsync(ct);
        var rows = await connection.QueryAsync<BranchAccountDefault>(new CommandDefinition("""
            SELECT branch_id AS BranchId, key AS Key, account_id AS AccountId,
                   updated_at AS UpdatedAt
            FROM branch_account_defaults WHERE branch_id = @branchId ORDER BY key
            """, new { branchId }, cancellationToken: ct));
        return rows.ToList();
    }

    public async Task<Guid> UpsertAsync(BranchAccountDefaultInput input, Guid userId,
        CancellationToken ct = default)
    {
        await using var connection = await _factory.OpenAsync(ct);
        return await connection.ExecuteScalarAsync<Guid>(new CommandDefinition("""
            INSERT INTO branch_account_defaults (branch_id, key, account_id, updated_by)
            VALUES (@BranchId, @Key, @AccountId, @userId)
            ON CONFLICT (branch_id, key) DO UPDATE
            SET account_id = EXCLUDED.account_id, updated_by = @userId, updated_at = now()
            RETURNING account_id
            """, new { input.BranchId, input.Key, input.AccountId, userId }, cancellationToken: ct));
    }

    public async Task<bool> DeleteAsync(Guid branchId, string key, CancellationToken ct = default)
    {
        await using var connection = await _factory.OpenAsync(ct);
        return await connection.ExecuteAsync(new CommandDefinition(
            "DELETE FROM branch_account_defaults WHERE branch_id = @branchId AND key = @key",
            new { branchId, key }, cancellationToken: ct)) > 0;
    }

    public async Task<Guid?> ResolveAsync(Guid branchId, string key, CancellationToken ct = default)
    {
        await using var connection = await _factory.OpenAsync(ct);
        return await connection.ExecuteScalarAsync<Guid?>(new CommandDefinition(
            "SELECT account_id FROM branch_account_defaults WHERE branch_id = @branchId AND key = @key",
            new { branchId, key }, cancellationToken: ct));
    }
}
