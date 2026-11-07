using Ubp.Core.Models;
using Ubp.Core.Results;
using Ubp.Core.Security;
using Ubp.Infrastructure.Repositories;

namespace Ubp.Application.Services;

/// <summary>
/// تعديل موحّد لبيانات الوردية من تقرير الوردية: اليوزر العادي يعدّل مسودات الوردية **المفتوحة**،
/// والمحاسب يعدّل ما لم يُرحَّل (وردية مُغلقة بانتظار المراجعة). يعدّل المستند الأصلي بكل آثاره.
/// </summary>
public sealed class ShiftEditService
{
    private readonly IShiftEditRepository _repo;
    private readonly ITreasuryRepository _treasury;
    private readonly PermissionService _permissions;
    private readonly AuditService _audit;

    public ShiftEditService(IShiftEditRepository repo, ITreasuryRepository treasury,
        PermissionService permissions, AuditService audit)
    {
        _repo = repo; _treasury = treasury; _permissions = permissions; _audit = audit;
    }

    public async Task<IReadOnlyList<ShiftEditableLine>> ListAsync(ActorContext actor, Guid shiftId,
        CancellationToken ct = default)
    {
        await _permissions.RequireAsync(actor, ScreenCodes.TreasuryShifts, ActionCodes.View, ct: ct);
        return await _repo.ListEditableAsync(shiftId, ct);
    }

    public async Task<ShiftEditOutcome> ApplyAsync(ActorContext actor, ShiftLineEditInput input,
        CancellationToken ct = default)
    {
        await _permissions.RequireAsync(actor, ScreenCodes.TreasuryShifts, ActionCodes.View, ct: ct);
        var shift = await _treasury.GetShiftAsync(input.ShiftId, ct)
                    ?? throw new NotFoundAppException();
        BranchScope.Ensure(actor, shift.BranchId);

        if (!shift.IsOpen)
        {
            // بعد الإغلاق: فقط المحاسب (اعتماد المراجعة) وقبل الترحيل.
            await _permissions.RequireAsync(actor, ScreenCodes.TreasuryShifts, ActionCodes.Approve,
                ct: ct);
            if (!shift.IsPendingReview)
                throw new ValidationAppException("validation.shift_edit_locked");
        }

        var outcome = await _repo.ApplyAsync(input, actor.UserId, ct);
        await _audit.WriteAsync(actor, "shift_edits", input.DocId.ToString(), "UPDATE", null,
            new { input.EntityType, input.LineId, input.Field, input.NewValue, input.Note }, ct);
        return outcome;
    }
}
