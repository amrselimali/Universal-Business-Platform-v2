using Dapper;
using Ubp.Core.Models;
using Ubp.Infrastructure.Data;

namespace Ubp.Infrastructure.Repositories;

public sealed partial class AssetRepository
{
    public async Task<Guid> CreatePlanAsync(Guid assetId, string kind, int? intervalDays,
        decimal? intervalUnits, DateTime? nextDueDate, decimal? nextDueMeter, Guid userId,
        CancellationToken ct = default)
    {
        await using var connection = await _factory.OpenAsync(ct);
        return await connection.ExecuteScalarAsync<Guid>(new CommandDefinition("""
            INSERT INTO asset_maintenance_plans
                (asset_id, kind, interval_days, interval_units, next_due_date, next_due_meter)
            VALUES (@assetId, @kind, @intervalDays, @intervalUnits, @nextDueDate, @nextDueMeter)
            RETURNING id
            """, new { assetId, kind, intervalDays, intervalUnits, nextDueDate, nextDueMeter },
            cancellationToken: ct));
    }

    public async Task<IReadOnlyList<MaintenanceOrder>> ListOrdersAsync(Guid assetId, int take = 50,
        CancellationToken ct = default)
    {
        await using var connection = await _factory.OpenAsync(ct);
        var rows = await connection.QueryAsync<MaintenanceOrder>(new CommandDefinition("""
            SELECT id AS Id, asset_id AS AssetId, plan_id AS PlanId,
                   maintenance_date AS MaintenanceDate, cost AS Cost, action AS Action,
                   status AS Status, reset_meter_value AS ResetMeterValue, notes AS Notes,
                   closed_at AS ClosedAt
            FROM asset_maintenance_orders WHERE asset_id = @assetId
            ORDER BY maintenance_date DESC, opened_at DESC LIMIT @take
            """, new { assetId, take = Math.Clamp(take, 1, 200) }, cancellationToken: ct));
        return rows.ToList();
    }

    public async Task<Guid> CreateOrderAsync(MaintenanceOrderInput input, Guid userId,
        CancellationToken ct = default)
    {
        await using var connection = await _factory.OpenAsync(ct);
        return await connection.ExecuteScalarAsync<Guid>(new CommandDefinition("""
            INSERT INTO asset_maintenance_orders
                (asset_id, plan_id, maintenance_date, cost, action, notes, created_by)
            VALUES (@AssetId, @PlanId, @MaintenanceDate, @Cost, @Action, @Notes, @userId)
            RETURNING id
            """, new { input.AssetId, input.PlanId, input.MaintenanceDate, input.Cost, input.Action,
                input.Notes, userId }, cancellationToken: ct));
    }

    /// <summary>يغلق أمر الصيانة ويعيد قيمة العداد المعاد تعيينه (أو null إن لم يكن مُصفَّراً).</summary>
    public async Task<bool> CloseOrderAsync(Guid orderId, decimal? resetMeterValue, Guid userId,
        CancellationToken ct = default)
    {
        await using var connection = await _factory.OpenAsync(ct);
        return await connection.ExecuteAsync(new CommandDefinition("""
            UPDATE asset_maintenance_orders
            SET status = 'DONE', closed_at = now(), reset_meter_value = @resetMeterValue
            WHERE id = @orderId AND status = 'OPEN'
            """, new { orderId, resetMeterValue }, cancellationToken: ct)) > 0;
    }

    public async Task<MaintenanceOrder?> GetOrderAsync(Guid orderId, CancellationToken ct = default)
    {
        await using var connection = await _factory.OpenAsync(ct);
        return await connection.QuerySingleOrDefaultAsync<MaintenanceOrder>(new CommandDefinition("""
            SELECT id AS Id, asset_id AS AssetId, plan_id AS PlanId,
                   maintenance_date AS MaintenanceDate, cost AS Cost, action AS Action,
                   status AS Status, reset_meter_value AS ResetMeterValue, notes AS Notes,
                   closed_at AS ClosedAt
            FROM asset_maintenance_orders WHERE id = @orderId
            """, new { orderId }, cancellationToken: ct));
    }
}
