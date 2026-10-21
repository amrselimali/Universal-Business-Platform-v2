using Dapper;
using Ubp.Core.Models;

namespace Ubp.Infrastructure.Repositories;

public sealed partial class PartnerRepository
{
    public async Task<Partner> CreateAsync(PartnerKind kind, PartnerInput input,
        CancellationToken ct = default)
    {
        var sql = $"""
            INSERT INTO {Table(kind)}
                (code, name_ar, name_en, category_id, scope_type, branch_id, phone, email,
                 address, tax_number, credit_limit, notes, is_active)
            VALUES
                (@Code, @NameAr, @NameEn, @CategoryId, @ScopeType, @BranchId, @Phone, @Email,
                 @Address, @TaxNumber, @CreditLimit, @Notes, @IsActive)
            RETURNING id
            """;
        await using var connection = await _factory.OpenAsync(ct);
        var id = await connection.ExecuteScalarAsync<Guid>(new CommandDefinition(sql, input, cancellationToken: ct));
        return (await GetAsync(kind, id, ct))!;
    }

    public async Task<Partner?> UpdateAsync(PartnerKind kind, Guid id, int version, PartnerInput input,
        CancellationToken ct = default)
    {
        var sql = $"""
            UPDATE {Table(kind)} SET
                code = @Code, name_ar = @NameAr, name_en = @NameEn, category_id = @CategoryId,
                scope_type = @ScopeType, branch_id = @BranchId, phone = @Phone, email = @Email,
                address = @Address, tax_number = @TaxNumber, credit_limit = @CreditLimit,
                notes = @Notes, is_active = @IsActive
            WHERE id = @id AND version = @version
            RETURNING id
            """;
        await using var connection = await _factory.OpenAsync(ct);
        var updated = await connection.ExecuteScalarAsync<Guid?>(new CommandDefinition(sql, new
        {
            id, version, input.Code, input.NameAr, input.NameEn, input.CategoryId, input.ScopeType,
            input.BranchId, input.Phone, input.Email, input.Address, input.TaxNumber,
            input.CreditLimit, input.Notes, input.IsActive
        }, cancellationToken: ct));
        return updated is null ? null : await GetAsync(kind, id, ct);
    }

    public async Task<bool> DeleteAsync(PartnerKind kind, Guid id, CancellationToken ct = default)
    {
        await using var connection = await _factory.OpenAsync(ct);
        return await connection.ExecuteAsync(new CommandDefinition(
            $"DELETE FROM {Table(kind)} WHERE id = @id", new { id }, cancellationToken: ct)) > 0;
    }

    public async Task<Partner?> SetActiveAsync(PartnerKind kind, Guid id, bool isActive,
        CancellationToken ct = default)
    {
        await using var connection = await _factory.OpenAsync(ct);
        var rows = await connection.ExecuteAsync(new CommandDefinition(
            $"UPDATE {Table(kind)} SET is_active = @isActive WHERE id = @id",
            new { id, isActive }, cancellationToken: ct));
        return rows == 0 ? null : await GetAsync(kind, id, ct);
    }

    public async Task<PartnerCategory> CreateCategoryAsync(PartnerKind kind, PartnerCategoryInput input,
        CancellationToken ct = default)
    {
        var sql = $"""
            INSERT INTO {CategoryTable(kind)} (code, name_ar, name_en, is_active)
            VALUES (@Code, @NameAr, @NameEn, @IsActive)
            RETURNING id
            """;
        await using var connection = await _factory.OpenAsync(ct);
        var id = await connection.ExecuteScalarAsync<Guid>(new CommandDefinition(sql, input, cancellationToken: ct));
        return (await GetCategoryAsync(kind, id, ct))!;
    }

    public async Task<PartnerCategory?> UpdateCategoryAsync(PartnerKind kind, Guid id, int version,
        PartnerCategoryInput input, CancellationToken ct = default)
    {
        var sql = $"""
            UPDATE {CategoryTable(kind)} SET code = @Code, name_ar = @NameAr, name_en = @NameEn,
                                             is_active = @IsActive
            WHERE id = @id AND version = @version
            RETURNING id
            """;
        await using var connection = await _factory.OpenAsync(ct);
        var updated = await connection.ExecuteScalarAsync<Guid?>(new CommandDefinition(sql, new
        {
            id, version, input.Code, input.NameAr, input.NameEn, input.IsActive
        }, cancellationToken: ct));
        return updated is null ? null : await GetCategoryAsync(kind, id, ct);
    }

    public async Task<bool> DeleteCategoryAsync(PartnerKind kind, Guid id, CancellationToken ct = default)
    {
        await using var connection = await _factory.OpenAsync(ct);
        return await connection.ExecuteAsync(new CommandDefinition(
            $"DELETE FROM {CategoryTable(kind)} WHERE id = @id", new { id }, cancellationToken: ct)) > 0;
    }
}
