using Ubp.Core.Models;
using Ubp.Core.Results;
using Ubp.Core.Security;

namespace Ubp.Application.Services;

/// <summary>تقرير الوردية ومراجعتها: عرض شامل للتسجيلات ثم اعتماد المحاسب.</summary>
public sealed partial class TreasuryService
{
    /// <summary>
    /// تقرير الوردية: تحصيلات وإيرادات وحركة خزائن، وعملاء بلا متابعة أو حجز مستقبلي،
    /// وحجوزات ومواعيد موظفي الغد، وكل ما سُجّل في الوردية (مسودات ومرحّل غير الملغى).
    /// </summary>
    public async Task<ShiftReport> GetShiftReportAsync(ActorContext actor, Guid shiftId,
        CancellationToken ct = default)
    {
        await _permissions.RequireAsync(actor, ScreenCodes.TreasuryShifts, ActionCodes.View, ct: ct);
        var report = await _treasury.GetShiftReportAsync(shiftId, ct);
        BranchScope.Ensure(actor, report.Shift.BranchId);
        return report;
    }

    public async Task<IReadOnlyList<ShiftReviewItem>> ListReviewItemsAsync(ActorContext actor,
        Guid shiftId, CancellationToken ct = default)
    {
        await _permissions.RequireAsync(actor, ScreenCodes.TreasuryShifts, ActionCodes.View, ct: ct);
        return await _treasury.ListReviewItemsAsync(shiftId, ct);
    }

    /// <summary>ترحيل الوردية للحسابات: تصبح «تحت المراجعة» ويُفعَّل اعتماد المراجعة — 0079.</summary>
    public async Task PostShiftToAccountsAsync(ActorContext actor, Guid shiftId,
        CancellationToken ct = default)
    {
        await _permissions.RequireAsync(actor, ScreenCodes.TreasuryShifts, ActionCodes.Approve,
            ct: ct);
        var shift = await _treasury.GetShiftAsync(shiftId, ct) ?? throw new NotFoundAppException();
        BranchScope.Ensure(actor, shift.BranchId);
        if (!await _treasury.PostShiftAsync(shiftId, actor.UserId, ct))
            throw new ValidationAppException("validation.shift_not_closed");
        await _audit.WriteAsync(actor, "cashier_shifts", shiftId.ToString(), "UPDATE", null,
            new { review_status = ShiftReviewStatuses.Pending }, ct);
    }

    /// <summary>
    /// اعتماد مراجعة الوردية: يوزر محاسب يملك صلاحية APPROVE (تُمنح وقت تكويد المستخدم)
    /// يغلق دورة المراجعة على كل تسجيلات الوردية.
    /// </summary>
    public async Task<Shift> ReviewShiftAsync(ActorContext actor, Guid shiftId,
        CancellationToken ct = default)
    {
        await _permissions.RequireAsync(actor, ScreenCodes.TreasuryShifts, ActionCodes.Approve,
            ct: ct);
        var shift = await _treasury.GetShiftAsync(shiftId, ct) ?? throw new NotFoundAppException();
        BranchScope.Ensure(actor, shift.BranchId);

        var reviewed = await _treasury.ReviewShiftAsync(shiftId, actor.UserId, ct);
        await _audit.WriteAsync(actor, "shift_review_items", shiftId.ToString(), "UPDATE",
            new { status = ShiftReviewStatuses.Pending },
            new { status = reviewed.ReviewStatus }, ct);
        return reviewed;
    }
}
