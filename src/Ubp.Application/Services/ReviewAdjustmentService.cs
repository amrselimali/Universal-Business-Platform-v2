using Ubp.Core.Models;
using Ubp.Core.Results;
using Ubp.Core.Security;
using Ubp.Infrastructure.Repositories;

namespace Ubp.Application.Services;

/// <summary>تعديلات المراجعة وملاحظاتها (تُطبَّق عند اعتماد الوردية بقيد الفرق) — 0084.</summary>
public sealed class ReviewAdjustmentService
{
    private readonly IReviewAdjustmentRepository _repo;
    private readonly PermissionService _permissions;
    private readonly AuditService _audit;

    public ReviewAdjustmentService(IReviewAdjustmentRepository repo,
        PermissionService permissions, AuditService audit)
    {
        _repo = repo; _permissions = permissions; _audit = audit;
    }

    public async Task<ReviewAdjustment> RecordAsync(ActorContext actor,
        ReviewAdjustmentInput input, CancellationToken ct = default)
    {
        await _permissions.RequireAsync(actor, ScreenCodes.TreasuryShifts, ActionCodes.Approve,
            ct: ct);
        var adjustment = await _repo.RecordAsync(input, actor.UserId, ct);
        await _audit.WriteAsync(actor, "review_adjustments", adjustment.Id.ToString(), "INSERT",
            null, new { adjustment.EntityType, adjustment.Field, adjustment.NewValue,
                adjustment.Note }, ct);
        return adjustment;
    }

    public async Task<IReadOnlyList<ReviewAdjustment>> ListAsync(ActorContext actor, Guid shiftId,
        CancellationToken ct = default)
    {
        await _permissions.RequireAsync(actor, ScreenCodes.TreasuryShifts, ActionCodes.View,
            ct: ct);
        return await _repo.ListAsync(shiftId, ct);
    }

    public async Task<IReadOnlyList<ReviewNote>> ListNotesAsync(ActorContext actor,
        string entityType, Guid entityId, CancellationToken ct = default)
    {
        await _permissions.RequireAsync(actor, ScreenCodes.TreasuryShifts, ActionCodes.View,
            ct: ct);
        return await _repo.ListNotesAsync(entityType, entityId, ct);
    }
}
