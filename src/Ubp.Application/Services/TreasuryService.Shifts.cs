using Npgsql;
using Ubp.Core.Models;
using Ubp.Core.Paging;
using Ubp.Core.Results;
using Ubp.Core.Security;
using Ubp.Infrastructure.Repositories;

namespace Ubp.Application.Services;

/// <summary>الخزائن وطرق الدفع والسندات والورديات: صلاحيات + تحقق + تدقيق.</summary>
public sealed partial class TreasuryService
{
    public async Task<IReadOnlyList<Shift>> ListShiftsAsync(ActorContext actor, Guid? branchId,
        int limit = 100, CancellationToken ct = default)
    {
        await _permissions.RequireAsync(actor, ScreenCodes.TreasuryShifts, ActionCodes.View, ct: ct);
        return await _treasury.ListShiftsAsync(branchId, limit, ct);
    }

    public async Task<Shift?> GetOpenShiftAsync(ActorContext actor, Guid treasuryId,
        CancellationToken ct = default)
    {
        await _permissions.RequireAsync(actor, ScreenCodes.TreasuryShifts, ActionCodes.View, ct: ct);
        return await _treasury.GetOpenShiftAsync(treasuryId, ct);
    }

    public async Task<Shift> OpenShiftAsync(ActorContext actor, ShiftInput input,
        CancellationToken ct = default)
    {
        await _permissions.RequireAsync(actor, ScreenCodes.TreasuryShifts, ActionCodes.Open, ct: ct);
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

    /// <summary>إغلاق وردية: الفرق (زيادة/عجز) يُرحّل ويُدوَّن في التدقيق.</summary>
    public async Task<Shift> CloseShiftAsync(ActorContext actor, Guid shiftId, decimal countedCash,
        CancellationToken ct = default)
    {
        await _permissions.RequireAsync(actor, ScreenCodes.TreasuryShifts, ActionCodes.Close, ct: ct);
        Shift shift;
        try
        {
            shift = await _treasury.CloseShiftAsync(shiftId, countedCash, actor.UserId, ct);
        }
        catch (PostgresException ex) { throw DbErrorMapper.Map(ex); }

        await _audit.WriteAsync(actor, "cashier_shifts", shiftId.ToString(), "UPDATE",
            null, new { shift.Status, shift.ExpectedCash, shift.CountedCash, shift.Variance }, ct);
        return shift;
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
