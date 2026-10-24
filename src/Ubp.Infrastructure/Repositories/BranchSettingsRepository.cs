using Ubp.Core.Models;
using Ubp.Infrastructure.Data;

namespace Ubp.Infrastructure.Repositories;

public interface IBranchSettingsRepository
{
    Task<BranchSettings?> GetAsync(Guid branchId, CancellationToken ct = default);
    Task<BranchSettings> UpsertAsync(Guid branchId, Guid? consumablesWarehouseId, Guid userId,
        CancellationToken ct = default);
}

public sealed class BranchSettingsRepository : RepositoryBase, IBranchSettingsRepository
{
    public BranchSettingsRepository(IDbConnectionFactory factory) : base(factory) { }

    public Task<BranchSettings?> GetAsync(Guid branchId, CancellationToken ct = default) =>
        SingleAsync<BranchSettings>("""
            SELECT b.branch_id AS BranchId, b.consumables_warehouse_id AS ConsumablesWarehouseId,
                   w.name_ar AS ConsumablesWarehouseName
            FROM branch_settings b LEFT JOIN warehouses w ON w.id = b.consumables_warehouse_id
            WHERE b.branch_id = @branchId
            """, new { branchId }, ct);

    public async Task<BranchSettings> UpsertAsync(Guid branchId, Guid? consumablesWarehouseId,
        Guid userId, CancellationToken ct = default)
    {
        await ExecuteRowAsync("""
            INSERT INTO branch_settings (branch_id, consumables_warehouse_id, updated_at, updated_by)
            VALUES (@branchId, @consumablesWarehouseId, now(), @userId)
            ON CONFLICT (branch_id) DO UPDATE
            SET consumables_warehouse_id = EXCLUDED.consumables_warehouse_id,
                updated_at = now(), updated_by = @userId
            """, new { branchId, consumablesWarehouseId, userId }, ct);

        return await GetAsync(branchId, ct)
               ?? new BranchSettings { BranchId = branchId, ConsumablesWarehouseId = consumablesWarehouseId };
    }
}
