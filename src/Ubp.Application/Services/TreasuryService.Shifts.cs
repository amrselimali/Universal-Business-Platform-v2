using Npgsql;
using Ubp.Core.Models;
using Ubp.Core.Results;
using Ubp.Core.Security;

namespace Ubp.Application.Services;

/// <summary>
/// الورديات (كل شاشات التطبيق وليس خزينة واحدة): صلاحيات + تحقق + تدقيق.
/// الوردية للفرع، واحدة مفتوحة فقط، ورصيد الافتتاح محسوب غير قابل للتعديل (0074).
/// </summary>
public sealed partial class TreasuryService
{
    public async Task<IReadOnlyList<Shift>> ListShiftsAsync(ActorContext actor, Guid? branchId,
        DateTime? from = null, DateTime? to = null, int limit = 200,
        CancellationToken ct = default)
    {
        await _permissions.RequireAsync(actor, ScreenCodes.TreasuryShifts, ActionCodes.View, ct: ct);
        if (branchId is { } value) BranchScope.Ensure(actor, value);
        return await _treasury.ListShiftsAsync(branchId, from, to, limit, ct);
    }

    /// <summary>عدّ النقدية المسجّل لكل خزينة في وردية (مسودة حتى اعتماد المحاسب).</summary>
    public async Task<IReadOnlyList<ShiftCashCount>> ListCashCountsAsync(ActorContext actor,
        Guid shiftId, CancellationToken ct = default)
    {
        await _permissions.RequireAsync(actor, ScreenCodes.TreasuryShifts, ActionCodes.View, ct: ct);
        return await _treasury.ListCashCountsAsync(shiftId, ct);
    }

    public async Task<Shift?> GetOpenShiftAsync(ActorContext actor, Guid branchId,
        CancellationToken ct = default)
    {
        await _permissions.RequireAsync(actor, ScreenCodes.TreasuryShifts, ActionCodes.View, ct: ct);
        BranchScope.Ensure(actor, branchId);
        return await _treasury.GetOpenShiftAsync(branchId, ct);
    }

    /// <summary>أرصدة فتح الوردية للفرع: كلها محسوبة ويُعرض الرقم إجبارياً بلا تعديل.</summary>
    public async Task<ShiftOpeningBalances> GetOpeningBalancesAsync(ActorContext actor,
        Guid branchId, CancellationToken ct = default)
    {
        await _permissions.RequireAsync(actor, ScreenCodes.TreasuryShifts, ActionCodes.View, ct: ct);
        BranchScope.Ensure(actor, branchId);
        return await _treasury.GetShiftOpeningBalancesAsync(branchId, ct);
    }

    public async Task<Shift> OpenShiftAsync(ActorContext actor, ShiftInput input,
        CancellationToken ct = default)
    {
        await _permissions.RequireAsync(actor, ScreenCodes.TreasuryShifts, ActionCodes.Open, ct: ct);
        if (input.BranchId == Guid.Empty)
            throw new ValidationAppException("validation.required_fields");
        BranchScope.Ensure(actor, input.BranchId);

        var id = Guid.CreateVersion7();
        try
        {
            await _treasury.OpenShiftAsync(id, input, actor.UserId, ct);
        }
        catch (PostgresException ex) { throw DbErrorMapper.Map(ex); }

        var shift = await _treasury.GetShiftAsync(id, ct) ?? throw new NotFoundAppException();
        await _audit.WriteAsync(actor, "cashier_shifts", id.ToString(), "INSERT", null, shift, ct);
        return shift;
    }

    /// <summary>
    /// إغلاق وردية: يُعرض تقرير الوردية أولاً، ثم يُرحّل كل ما سُجّل للمراجعة.
    /// countedCash = null يعني إغلاقاً آلياً (5 صباحاً) بمطابقة المتوقع.
    /// </summary>
    public async Task<Shift> CloseShiftAsync(ActorContext actor, Guid shiftId,
        IReadOnlyList<ShiftCashCountInput>? counts = null, CancellationToken ct = default)
    {
        await _permissions.RequireAsync(actor, ScreenCodes.TreasuryShifts, ActionCodes.Close, ct: ct);
        Shift shift;
        try
        {
            shift = await _treasury.CloseShiftAsync(shiftId, counts, actor.UserId,
                autoClosed: false, ct);
        }
        catch (PostgresException ex) { throw DbErrorMapper.Map(ex); }

        await _audit.WriteAsync(actor, "cashier_shifts", shiftId.ToString(), "UPDATE", null,
            new
            {
                shift.Status, shift.ExpectedCash, shift.CountedCash, shift.Variance,
                shift.ReviewStatus
            }, ct);
        return shift;
    }

    /// <summary>إغلاق آلي لكل وردية تجاوزت الخامسة صباحاً (يستدعيه العامل الخلفي).</summary>
    public async Task<int> AutoCloseAsync(DateTimeOffset cutoff, CancellationToken ct = default)
    {
        var due = await _treasury.ListShiftsDueForAutoCloseAsync(cutoff, ct);
        var closed = 0;
        foreach (var shift in due)
        {
            try
            {
                await _treasury.CloseShiftAsync(shift.Id, counts: null, shift.UserId,
                    autoClosed: true, ct);
                closed++;
            }
            catch (AppException)
            {
                // وردية تغيّرت حالتها بين القراءة والإغلاق — تُترك للدورة التالية.
            }
        }
        return closed;
    }

    public async Task SaveDenominationsAsync(ActorContext actor, Guid shiftId,
        IReadOnlyList<ShiftDenominationInput> denominations, CancellationToken ct = default)
    {
        await _permissions.RequireAsync(actor, ScreenCodes.TreasuryShifts, ActionCodes.Open, ct: ct);
        await _treasury.SaveDenominationsAsync(shiftId, denominations, ct);
        await _audit.WriteAsync(actor, "shift_denominations", shiftId.ToString(), "UPDATE", null,
            denominations, ct);
    }

    public async Task<IReadOnlyList<ShiftDenomination>> ListDenominationsAsync(ActorContext actor,
        Guid shiftId, CancellationToken ct = default)
    {
        await _permissions.RequireAsync(actor, ScreenCodes.TreasuryShifts, ActionCodes.View, ct: ct);
        return await _treasury.ListDenominationsAsync(shiftId, ct);
    }

    /// <summary>إعادة فتح وردية مغلقة بسبب موثّق وعكس قيد فرقها.</summary>
    public async Task<Shift> ReopenShiftAsync(ActorContext actor, Guid shiftId, string reason,
        CancellationToken ct = default)
    {
        await _permissions.RequireAsync(actor, ScreenCodes.TreasuryShifts, ActionCodes.Reopen,
            ct: ct);
        if (string.IsNullOrWhiteSpace(reason))
            throw new ValidationAppException("validation.shift_reason_required");

        Shift shift;
        try
        {
            shift = await _treasury.ReopenShiftAsync(shiftId, reason, actor.UserId, ct);
        }
        catch (PostgresException ex) { throw DbErrorMapper.Map(ex); }

        await _audit.WriteAsync(actor, "cashier_shifts", shiftId.ToString(), "UPDATE",
            new { status = ShiftStatuses.Closed }, new { shift.Status, reason }, ct);
        return shift;
    }
}

