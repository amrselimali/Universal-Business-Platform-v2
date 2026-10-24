using Dapper;
using Npgsql;
using Ubp.Core.Models;
using Ubp.Core.Results;
using Ubp.Infrastructure.Data;

namespace Ubp.Infrastructure.Repositories;

public interface ITreasuryRepository
{
    Task<Guid> CreateTreasuryAsync(Guid id, TreasuryInput input, Guid userId, CancellationToken ct = default);
    Task<Guid> CreateMethodAsync(Guid id, TreasuryMethodInput input, Guid userId, CancellationToken ct = default);
    Task<IReadOnlyList<Treasury>> ListTreasuriesAsync(Guid? branchId, CancellationToken ct = default);
    Task<IReadOnlyList<TreasuryMethod>> ListMethodsAsync(Guid treasuryId, CancellationToken ct = default);
}

public sealed partial class TreasuryRepository : ITreasuryRepository
{
    private readonly IDbConnectionFactory _factory;
    public TreasuryRepository(IDbConnectionFactory factory) => _factory = factory;

    public async Task<Guid> CreateTreasuryAsync(Guid id, TreasuryInput input, Guid userId,
        CancellationToken ct = default)
    {
        await using var connection = await _factory.OpenAsync(ct);
        return await connection.ExecuteScalarAsync<Guid>(new CommandDefinition("""
            INSERT INTO treasuries (id, branch_id, code, name_ar, name_en, treasury_type, account_id,
                opening_balance, created_by)
            VALUES (@id, @BranchId, @Code, @NameAr, @NameEn, @TreasuryType, @AccountId,
                    @OpeningBalance, @userId)
            RETURNING id
            """, new { id, input.BranchId, input.Code, input.NameAr, input.NameEn,
                input.TreasuryType, input.AccountId, input.OpeningBalance, userId },
            cancellationToken: ct));
    }

    public async Task<Guid> CreateMethodAsync(Guid id, TreasuryMethodInput input, Guid userId,
        CancellationToken ct = default)
    {
        await using var connection = await _factory.OpenAsync(ct);
        return await connection.ExecuteScalarAsync<Guid>(new CommandDefinition("""
            INSERT INTO treasury_methods (id, treasury_id, code, name_ar, name_en, method_type,
                clearing_account_id, fee_percent, fee_fixed, requires_reference, created_by)
            VALUES (@id, @TreasuryId, @Code, @NameAr, @NameEn, @MethodType, @ClearingAccountId,
                    @FeePercent, @FeeFixed, @RequiresReference, @userId)
            RETURNING id
            """, new { id, input.TreasuryId, input.Code, input.NameAr, input.NameEn,
                input.MethodType, input.ClearingAccountId, input.FeePercent, input.FeeFixed,
                input.RequiresReference, userId }, cancellationToken: ct));
    }

    public async Task<IReadOnlyList<Treasury>> ListTreasuriesAsync(Guid? branchId,
        CancellationToken ct = default)
    {
        await using var connection = await _factory.OpenAsync(ct);
        return (await connection.QueryAsync<Treasury>(new CommandDefinition("""
            SELECT t.id AS Id, t.branch_id AS BranchId, t.code AS Code, t.name_ar AS NameAr,
                   t.name_en AS NameEn, t.treasury_type AS TreasuryType, t.account_id AS AccountId,
                   a.code AS AccountCode, a.name_ar AS AccountName, t.opening_balance AS OpeningBalance,
                   t.is_active AS IsActive
            FROM treasuries t JOIN accounts a ON a.id = t.account_id
            WHERE t.is_active AND (@branchId::uuid IS NULL OR t.branch_id = @branchId::uuid)
            ORDER BY t.code
            """, new { branchId }, cancellationToken: ct))).ToList();
    }

    public async Task<IReadOnlyList<TreasuryMethod>> ListMethodsAsync(Guid treasuryId,
        CancellationToken ct = default)
    {
        await using var connection = await _factory.OpenAsync(ct);
        return (await connection.QueryAsync<TreasuryMethod>(new CommandDefinition("""
            SELECT m.id AS Id, m.treasury_id AS TreasuryId, m.code AS Code, m.name_ar AS NameAr,
                   m.method_type AS MethodType, m.clearing_account_id AS ClearingAccountId,
                   m.fee_percent AS FeePercent, m.fee_fixed AS FeeFixed,
                   m.requires_reference AS RequiresReference, m.is_active AS IsActive
            FROM treasury_methods m WHERE m.treasury_id = @treasuryId AND m.is_active
            ORDER BY m.code
            """, new { treasuryId }, cancellationToken: ct))).ToList();
    }
}
