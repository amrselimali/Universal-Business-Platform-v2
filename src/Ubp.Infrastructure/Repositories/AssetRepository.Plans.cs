using Dapper;
using Ubp.Core.Models;
using Ubp.Infrastructure.Data;

namespace Ubp.Infrastructure.Repositories;

public sealed partial class AssetRepository
{
    public async Task<IReadOnlyList<MaintenancePlan>> ListPlansAsync(Guid assetId,
        CancellationToken ct = default)
    {
        await using var connection = await _factory.OpenAsync(ct);
        var rows = await connection.QueryAsync<MaintenancePlan>(new CommandDefinition("""
            SELECT id AS Id, asset_id AS AssetId, kind AS Kind, interval_days AS IntervalDays,
                   interval_units AS IntervalUnits, next_due_date AS NextDueDate,
                   next_due_meter AS NextDueMeter, is_active AS IsActive
            FROM asset_maintenance_plans WHERE asset_id = @assetId AND is_active
            ORDER BY created_at
            """, new { assetId }, cancellationToken: ct));
        return rows.ToList();
    }

    public async Task<IReadOnlyList<DepreciationRunRow>> ListRunsAsync(Guid? branchId, int take = 24,
        CancellationToken ct = default)
    {
        await using var connection = await _factory.OpenAsync(ct);
        var rows = await connection.QueryAsync<DepreciationRunRow>(new CommandDefinition("""
            SELECT r.id AS Id, r.period_month AS PeriodMonth, r.branch_id AS BranchId,
                   r.total_amount AS TotalAmount, r.journal_entry_id AS JournalEntryId,
                   e.entry_no AS EntryNo, r.run_at AS RunAt,
                   (SELECT count(*)::int FROM asset_depreciation_lines l WHERE l.run_id = r.id)
                       AS AssetsCount
            FROM asset_depreciation_runs r
            LEFT JOIN journal_entries e ON e.id = r.journal_entry_id
            WHERE (@branchId IS NULL OR r.branch_id = @branchId)
            ORDER BY r.period_month DESC
            LIMIT @take
            """, new { branchId, take = Math.Clamp(take, 1, 120) }, cancellationToken: ct));
        return rows.ToList();
    }
}
