using Dapper;
using Ubp.Core.Models;
using Ubp.Infrastructure.Data;

namespace Ubp.Infrastructure.Repositories;

public sealed partial class AssetRepository
{
    public async Task<Guid> CreateAsync(AssetInput input, Guid userId, CancellationToken ct = default)
    {
        const string sql = """
            INSERT INTO fixed_assets
                (code, name_ar, category_id, branch_id, vendor_id, purchase_date, in_service_date,
                 cost, salvage_value, is_metered, meter_type, meter_resets_on_maintenance, created_by)
            VALUES (@Code, @NameAr, @CategoryId, @BranchId, @VendorId, @PurchaseDate, @InServiceDate,
                    @Cost, @SalvageValue, @IsMetered, @MeterType, @MeterResetsOnMaintenance, @userId)
            RETURNING id
            """;
        await using var connection = await _factory.OpenAsync(ct);
        return await connection.ExecuteScalarAsync<Guid>(new CommandDefinition(sql, new
        {
            input.Code, input.NameAr, input.CategoryId, input.BranchId, input.VendorId,
            input.PurchaseDate, input.InServiceDate, input.Cost, input.SalvageValue,
            input.IsMetered,
            MeterType = input.IsMetered ? MeterTypes.Normalize(input.MeterType) : null,
            input.MeterResetsOnMaintenance, userId
        }, cancellationToken: ct));
    }

    public async Task<FixedAsset?> UpdateAsync(Guid id, int version, AssetInput input, Guid userId,
        CancellationToken ct = default)
    {
        const string sql = """
            UPDATE fixed_assets SET
                code = @Code, name_ar = @NameAr, category_id = @CategoryId, branch_id = @BranchId,
                vendor_id = @VendorId, purchase_date = @PurchaseDate,
                in_service_date = @InServiceDate, cost = @Cost, salvage_value = @SalvageValue,
                is_metered = @IsMetered, meter_type = @MeterType,
                meter_resets_on_maintenance = @MeterResetsOnMaintenance,
                updated_by = @userId, updated_at = now(), version = version + 1
            WHERE id = @id AND version = @version AND status <> 'DISPOSED'
            RETURNING id
            """;
        await using var connection = await _factory.OpenAsync(ct);
        var updated = await connection.ExecuteScalarAsync<Guid?>(new CommandDefinition(sql, new
        {
            id, version, input.Code, input.NameAr, input.CategoryId, input.BranchId, input.VendorId,
            input.PurchaseDate, input.InServiceDate, input.Cost, input.SalvageValue,
            input.IsMetered,
            MeterType = input.IsMetered ? MeterTypes.Normalize(input.MeterType) : null,
            input.MeterResetsOnMaintenance, userId
        }, cancellationToken: ct));
        return updated is null ? null : await GetAsync(id, ct);
    }

    public async Task<bool> SetStatusAsync(Guid id, string status, Guid userId,
        CancellationToken ct = default)
    {
        await using var connection = await _factory.OpenAsync(ct);
        return await connection.ExecuteAsync(new CommandDefinition("""
            UPDATE fixed_assets SET status = @status, updated_by = @userId, updated_at = now(),
                   version = version + 1,
                   disposed_at = CASE WHEN @status = 'DISPOSED' THEN now() ELSE disposed_at END
            WHERE id = @id AND status <> 'DISPOSED'
            """, new { id, status, userId }, cancellationToken: ct)) > 0;
    }
}
