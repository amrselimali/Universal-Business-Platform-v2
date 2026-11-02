using Ubp.Core.Models;
using Ubp.Core.Results;
using Ubp.Core.Security;

namespace Ubp.Application.Services;

public sealed partial class AssetService
{
    /// <summary>قراءة عدّاد: تصاعدية دائماً (أو تصفير عند الصيانة)، والفرق يُحسب تلقائياً.</summary>
    public async Task<AssetMeterReading> AddReadingAsync(ActorContext actor, Guid assetId,
        decimal value, string? note = null, string source = MeterSources.Manual,
        CancellationToken ct = default)
    {
        await _permissions.RequireAsync(actor, ScreenCodes.Assets, ActionCodes.AddMeterReading, ct: ct);
        var asset = await _assets.GetAsync(assetId, ct) ?? throw new NotFoundAppException();
        EnsureVisible(actor, asset);

        if (!asset.IsMetered)
            throw new ValidationAppException("validation.asset_not_metered");
        if (asset.Status == AssetStatuses.Disposed)
            throw new ValidationAppException("validation.asset_disposed");
        if (value < 0) throw new ValidationAppException("validation.meter_value_invalid");

        var isReset = source == MeterSources.MaintenanceReset;
        if (!isReset && value < asset.CurrentMeterValue)
            throw new ValidationAppException("validation.meter_must_increase");

        var delta = isReset ? 0 : value - asset.CurrentMeterValue;
        var id = await _assets.AddReadingAsync(assetId, value, delta, source, note, actor.UserId, ct);
        await _assets.SetMeterValueAsync(assetId, isReset ? 0 : value, actor.UserId, ct);

        return new AssetMeterReading
        {
            Id = id, AssetId = assetId, ReadingAt = DateTimeOffset.UtcNow,
            Value = value, Delta = delta, Source = source, Note = note
        };
    }

    /// <summary>إضافة وحدات استهلاك للعدّاد (يومية/تنفيذ) مع منع تكرار نفس سطر المصدر.</summary>
    public async Task<decimal> AddUsageAsync(ActorContext actor, Guid assetId, decimal units,
        Guid? sourceLineId = null, string? note = null, CancellationToken ct = default)
    {
        await _permissions.RequireAsync(actor, ScreenCodes.Assets, ActionCodes.AddMeterReading, ct: ct);
        var asset = await _assets.GetAsync(assetId, ct) ?? throw new NotFoundAppException();
        EnsureVisible(actor, asset);
        if (!asset.IsMetered) throw new ValidationAppException("validation.asset_not_metered");
        if (asset.Status == AssetStatuses.Disposed)
            throw new ValidationAppException("validation.asset_disposed");
        if (units <= 0) throw new ValidationAppException("validation.quantity_invalid");
        return await _assets.AddUsageAsync(assetId, units, sourceLineId, note, actor.UserId, ct);
    }

    /// <summary>تصفير عدّاد الأصل مع تسجيل قراءة تصفير.</summary>
    public async Task<decimal> ResetMeterAsync(ActorContext actor, Guid assetId,
        string? note = null, CancellationToken ct = default)
    {
        await _permissions.RequireAsync(actor, ScreenCodes.Assets, ActionCodes.AddMeterReading, ct: ct);
        var asset = await _assets.GetAsync(assetId, ct) ?? throw new NotFoundAppException();
        EnsureVisible(actor, asset);
        if (!asset.IsMetered) throw new ValidationAppException("validation.asset_not_metered");
        return await _assets.ResetMeterAsync(assetId, note, actor.UserId, ct);
    }

    public async Task<IReadOnlyList<AssetMeterReading>> ReadingsAsync(ActorContext actor,
        Guid assetId, CancellationToken ct = default)
    {
        await _permissions.RequireAsync(actor, ScreenCodes.Assets, ActionCodes.View, ct: ct);
        var asset = await _assets.GetAsync(assetId, ct) ?? throw new NotFoundAppException();
        EnsureVisible(actor, asset);
        return await _assets.ListReadingsAsync(assetId, ct: ct);
    }
}
