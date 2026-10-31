using Ubp.Core.Models;
using Ubp.Core.Results;
using Ubp.Core.Security;

namespace Ubp.Application.Services;

public sealed partial class AssetService
{
    public async Task<IReadOnlyList<MaintenanceOrder>> OrdersAsync(ActorContext actor, Guid assetId,
        CancellationToken ct = default)
    {
        await _permissions.RequireAsync(actor, ScreenCodes.Assets, ActionCodes.View, ct: ct);
        var asset = await _assets.GetAsync(assetId, ct) ?? throw new NotFoundAppException();
        EnsureVisible(actor, asset);
        return await _assets.ListOrdersAsync(assetId, ct: ct);
    }

    public async Task<Guid> OpenOrderAsync(ActorContext actor, MaintenanceOrderInput input,
        CancellationToken ct = default)
    {
        await _permissions.RequireAsync(actor, ScreenCodes.Assets, ActionCodes.OpenMaintenance, ct: ct);
        var asset = await _assets.GetAsync(input.AssetId, ct) ?? throw new NotFoundAppException();
        EnsureVisible(actor, asset);
        if (asset.Status == AssetStatuses.Disposed)
            throw new ValidationAppException("validation.asset_disposed");
        if (input.Cost < 0) throw new ValidationAppException("validation.asset_cost_invalid");

        var id = await _assets.CreateOrderAsync(input, actor.UserId, ct);
        await _audit.WriteAsync(actor, "asset_maintenance_orders", id.ToString(), "INSERT", null,
            new { input.AssetId, input.Cost, Status = MaintenanceStatuses.Open }, ct);
        return id;
    }

    /// <summary>
    /// إغلاق أمر الصيانة: يصبح DONE، ويُصفَّر العدّاد **فقط** إذا كان الأصل مُعدّاً
    /// (`meter_resets_on_maintenance`)، وتُسجَّل قراءة MAINTENANCE_RESET بقيمة 0.
    /// </summary>
    public async Task CloseOrderAsync(ActorContext actor, Guid orderId, CancellationToken ct = default)
    {
        await _permissions.RequireAsync(actor, ScreenCodes.Assets, ActionCodes.CloseMaintenance, ct: ct);
        var order = await _assets.GetOrderAsync(orderId, ct) ?? throw new NotFoundAppException();
        var asset = await _assets.GetAsync(order.AssetId, ct) ?? throw new NotFoundAppException();
        EnsureVisible(actor, asset);

        if (order.Status != MaintenanceStatuses.Open)
            throw new ValidationAppException("validation.maintenance_not_open");

        var reset = asset.IsMetered && asset.MeterResetsOnMaintenance;
        if (reset)
        {
            await _assets.AddReadingAsync(asset.Id, 0, 0, MeterSources.MaintenanceReset,
                "تصفير العدّاد بعد الصيانة", actor.UserId, ct);
            await _assets.SetMeterValueAsync(asset.Id, 0, actor.UserId, ct);
        }

        if (!await _assets.CloseOrderAsync(orderId, reset ? 0 : null, actor.UserId, ct))
            throw new ValidationAppException("validation.maintenance_not_open");

        await _audit.WriteAsync(actor, "asset_maintenance_orders", orderId.ToString(), "UPDATE",
            new { Status = MaintenanceStatuses.Open },
            new { Status = MaintenanceStatuses.Done, MeterReset = reset }, ct);
    }
}
