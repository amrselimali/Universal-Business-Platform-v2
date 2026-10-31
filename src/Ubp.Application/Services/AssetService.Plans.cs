using Ubp.Core.Models;
using Ubp.Core.Results;
using Ubp.Core.Security;
using Ubp.Infrastructure.Repositories;

namespace Ubp.Application.Services;

public sealed record MaintenancePlanInput(Guid AssetId, string Kind, int? IntervalDays,
    decimal? IntervalUnits, DateTime? NextDueDate, decimal? NextDueMeter);

public sealed partial class AssetService
{
    public async Task<IReadOnlyList<MaintenancePlan>> PlansAsync(ActorContext actor, Guid assetId,
        CancellationToken ct = default)
    {
        await _permissions.RequireAsync(actor, ScreenCodes.Assets, ActionCodes.View, ct: ct);
        var asset = await _assets.GetAsync(assetId, ct) ?? throw new NotFoundAppException();
        EnsureVisible(actor, asset);
        return await _assets.ListPlansAsync(assetId, ct);
    }

    /// <summary>خطة صيانة زمنية (كل عدد أيام) أو بالعدّاد (كل عدد وحدات).</summary>
    public async Task<Guid> CreatePlanAsync(ActorContext actor, MaintenancePlanInput input,
        CancellationToken ct = default)
    {
        await _permissions.RequireAsync(actor, ScreenCodes.Assets, ActionCodes.OpenMaintenance, ct: ct);
        var asset = await _assets.GetAsync(input.AssetId, ct) ?? throw new NotFoundAppException();
        EnsureVisible(actor, asset);

        var kind = input.Kind == MaintenanceKinds.Meter ? MaintenanceKinds.Meter : MaintenanceKinds.Time;
        if (kind == MaintenanceKinds.Time && input.IntervalDays is null or <= 0)
            throw new ValidationAppException("validation.maintenance_interval_required");
        if (kind == MaintenanceKinds.Meter && input.IntervalUnits is null or <= 0)
            throw new ValidationAppException("validation.maintenance_interval_required");
        if (kind == MaintenanceKinds.Meter && !asset.IsMetered)
            throw new ValidationAppException("validation.asset_not_metered");

        var id = await _assets.CreatePlanAsync(asset.Id, kind,
            kind == MaintenanceKinds.Time ? input.IntervalDays : null,
            kind == MaintenanceKinds.Meter ? input.IntervalUnits : null,
            input.NextDueDate, input.NextDueMeter, actor.UserId, ct);

        await _audit.WriteAsync(actor, "asset_maintenance_plans", id.ToString(), "INSERT", null,
            new { assetId = asset.Id, kind, input.IntervalDays, input.IntervalUnits }, ct);
        return id;
    }

    /// <summary>سجل تشغيلات الإهلاك (مصدره جدول التشغيلات + رقم القيد من اليومية).</summary>
    public async Task<IReadOnlyList<DepreciationRunRow>> RunsAsync(ActorContext actor,
        Guid? branchId = null, CancellationToken ct = default)
    {
        await _permissions.RequireAsync(actor, ScreenCodes.Assets, ActionCodes.View, ct: ct);
        if (branchId is { } branch) BranchScope.Ensure(actor, branch);
        var scope = branchId ?? (actor.IsSuperAdmin ? null : actor.CurrentBranchId);
        return await _assets.ListRunsAsync(scope, ct: ct);
    }
}
