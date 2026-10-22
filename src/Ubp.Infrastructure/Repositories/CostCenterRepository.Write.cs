using Dapper;
using Ubp.Core.Models;
using Ubp.Infrastructure.Data;

namespace Ubp.Infrastructure.Repositories;

public sealed partial class CostCenterRepository
{
    public async Task<Guid> CreateAsync(Guid id, CostCenterInput input, string path, short level,
        Guid userId, CancellationToken ct = default)
    {
        await using var connection = await _factory.OpenAsync(ct);
        return await connection.ExecuteScalarAsync<Guid>(new CommandDefinition("""
            INSERT INTO cost_centers (id, code, name_ar, name_en, parent_id, path, level, is_group,
                                      branch_id, notes, is_active, created_by)
            VALUES (@id, @Code, @NameAr, @NameEn, @ParentId, @path, @level, @IsGroup,
                    @BranchId, @Notes, @IsActive, @userId)
            RETURNING id
            """, new
        {
            id, input.Code, input.NameAr, input.NameEn, input.ParentId, path, level, input.IsGroup,
            input.BranchId, input.Notes, input.IsActive, userId
        }, cancellationToken: ct));
    }

    public async Task<CostCenter?> UpdateAsync(Guid id, int version, CostCenterInput input,
        Guid userId, CancellationToken ct = default)
    {
        await using var connection = await _factory.OpenAsync(ct);
        var updated = await connection.ExecuteScalarAsync<Guid?>(new CommandDefinition("""
            UPDATE cost_centers SET code = @Code, name_ar = @NameAr, name_en = @NameEn,
                                    is_group = @IsGroup, branch_id = @BranchId, notes = @Notes,
                                    is_active = @IsActive, updated_by = @userId
            WHERE id = @id AND version = @version
            RETURNING id
            """, new
        {
            id, version, input.Code, input.NameAr, input.NameEn, input.IsGroup, input.BranchId,
            input.Notes, input.IsActive, userId
        }, cancellationToken: ct));
        return updated is null ? null : await GetAsync(id, ct);
    }

    public async Task<CostCenter?> SetActiveAsync(Guid id, bool isActive, Guid userId,
        CancellationToken ct = default)
    {
        await using var connection = await _factory.OpenAsync(ct);
        var rows = await connection.ExecuteAsync(new CommandDefinition(
            "UPDATE cost_centers SET is_active = @isActive, updated_by = @userId WHERE id = @id",
            new { id, isActive, userId }, cancellationToken: ct));
        return rows == 0 ? null : await GetAsync(id, ct);
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        await using var connection = await _factory.OpenAsync(ct);
        return await connection.ExecuteAsync(new CommandDefinition(
            "DELETE FROM cost_centers WHERE id = @id", new { id }, cancellationToken: ct)) > 0;
    }
}
