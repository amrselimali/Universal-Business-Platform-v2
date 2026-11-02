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
}
