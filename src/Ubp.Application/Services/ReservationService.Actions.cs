using Ubp.Core.Models;
using Ubp.Core.Results;
using Ubp.Core.Security;

namespace Ubp.Application.Services;

public sealed partial class ReservationService
{
    /// <summary>تحرير الحجز: إلغاء الحجز (يُحرّر الكميات المحجوزة) مع تسجيل الحدث.</summary>
    public async Task ReleaseAsync(ActorContext actor, Guid bookingId, string reason,
        CancellationToken ct = default)
    {
        await _permissions.RequireAsync(actor, ScreenCodes.Reservations,
            ActionCodes.ReleaseReservation, ct: ct);
        var booking = await _bookings.GetAsync(actor, bookingId, ct) ?? throw new NotFoundAppException();
        BranchScope.Ensure(actor, booking.BranchId);

        await _bookings.CancelAsync(actor, bookingId,
            string.IsNullOrWhiteSpace(reason) ? "تحرير حجز" : reason.Trim(), ct);
        await _reservations.InsertAdjustmentAsync(bookingId, null, ReservationAdjustmentKinds.Release,
            0, 0, null, null, reason, actor.UserId, ct);
    }

    /// <summary>تمديد صلاحية الحجز دون لمس الكميات أو القيود.</summary>
    public async Task ExtendAsync(ActorContext actor, Guid bookingId, DateTimeOffset newExpiry,
        CancellationToken ct = default)
    {
        await _permissions.RequireAsync(actor, ScreenCodes.Reservations,
            ActionCodes.ExtendReservation, ct: ct);
        var booking = await _bookings.GetAsync(actor, bookingId, ct) ?? throw new NotFoundAppException();
        BranchScope.Ensure(actor, booking.BranchId);

        if (!await _reservations.ExtendAsync(bookingId, newExpiry, ct))
            throw new ValidationAppException("validation.booking_not_extendable");

        await _reservations.InsertAdjustmentAsync(bookingId, null, ReservationAdjustmentKinds.Extend,
            0, 0, null, null, $"تمديد إلى {newExpiry:yyyy-MM-dd}", actor.UserId, ct);
    }
}
