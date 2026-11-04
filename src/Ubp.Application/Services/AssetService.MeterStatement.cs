using Ubp.Core.Models;
using Ubp.Core.Results;
using Ubp.Core.Security;

namespace Ubp.Application.Services;

public sealed partial class AssetService
{
    /// <summary>
    /// كشف متابعة العدّاد: إجماليات بالشهر أو باليوم أو يوم محدد، مع بداية المدة والمستهلك
    /// والتسويات والرصيد، إضافةً إلى الحركات التفصيلية (قراءات + تسويات) داخل الفترة.
    /// </summary>
    public async Task<AssetMeterStatement> MeterStatementAsync(ActorContext actor, Guid assetId,
        string mode, DateTime from, DateTime? to = null, CancellationToken ct = default)
    {
        await _permissions.RequireAsync(actor, ScreenCodes.Assets, ActionCodes.View, ct: ct);
        var asset = await _assets.GetAsync(assetId, ct) ?? throw new NotFoundAppException();
        EnsureVisible(actor, asset);
        if (!asset.IsMetered)
            throw new ValidationAppException("validation.asset_not_metered");

        mode = MeterStatementModes.IsValid(mode) ? mode : MeterStatementModes.Month;
        var (start, end) = ResolveRange(mode, from, to);

        var readings = await _assets.ListReadingsUpToAsync(assetId, end, ct);
        var adjustments = await _assets.ListAdjustmentsUpToAsync(assetId, end, ct);

        return new AssetMeterStatement
        {
            AssetId = asset.Id, AssetCode = asset.Code, AssetName = asset.NameAr,
            BranchName = asset.BranchName, MeterType = asset.MeterType,
            CurrentValue = asset.CurrentMeterValue, Mode = mode, From = start, To = end,
            Rows = BuildRows(mode, start, end, readings, adjustments),
            Movements = BuildMovements(start, end, readings, adjustments)
        };
    }

    /// <summary>إضافة تسوية عدّاد (وحدات +/- مع ملاحظة إلزامية) في تاريخ محدد.</summary>
    public async Task<AssetMeterAdjustment> AddMeterAdjustmentAsync(ActorContext actor,
        AssetMeterAdjustmentInput input, CancellationToken ct = default)
    {
        await _permissions.RequireAsync(actor, ScreenCodes.Assets, ActionCodes.AddMeterReading,
            ct: ct);
        var asset = await _assets.GetAsync(input.AssetId, ct) ?? throw new NotFoundAppException();
        EnsureVisible(actor, asset);
        if (!asset.IsMetered)
            throw new ValidationAppException("validation.asset_not_metered");
        if (asset.Status == AssetStatuses.Disposed)
            throw new ValidationAppException("validation.asset_disposed");
        if (input.Units == 0)
            throw new ValidationAppException("validation.meter_adjustment_units");

        var note = (input.Note ?? string.Empty).Trim();
        if (note.Length == 0)
            throw new ValidationAppException("validation.meter_adjustment_note");

        var at = input.AdjustedAt.Date.AddHours(12);
        var id = await _assets.AddAdjustmentAsync(input.AssetId, at, input.Units,
            note.Length > 300 ? note[..300] : note, actor.UserId, ct);

        var adjustment = new AssetMeterAdjustment
        {
            Id = id, AssetId = input.AssetId, AdjustedAt = new DateTimeOffset(at),
            Units = input.Units, Note = note,
            CreatedAt = DateTimeOffset.UtcNow
        };
        await _audit.WriteAsync(actor, "asset_meter_adjustments", id.ToString(), "INSERT", null,
            adjustment, ct);
        return adjustment;
    }
}
