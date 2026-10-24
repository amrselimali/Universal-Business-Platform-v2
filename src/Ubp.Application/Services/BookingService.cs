using Npgsql;
using Ubp.Core.Models;
using Ubp.Core.Paging;
using Ubp.Core.Results;
using Ubp.Core.Security;
using Ubp.Infrastructure.Repositories;

namespace Ubp.Application.Services;

/// <summary>
/// الحجوزات: الالتزام بلا قيد محاسبي (D2)، التأكيد يحجز المتاح عند تفعيل الإعداد (D21)،
/// والتنفيذ يحدّث الكميات المنجزة ويولّد التكلفة.
/// </summary>
public sealed class BookingService
{
    private readonly IBookingRepository _bookings;
    private readonly ISettingsService _settings;
    private readonly PermissionService _permissions;
    private readonly AuditService _audit;

    public BookingService(IBookingRepository bookings, ISettingsService settings,
        PermissionService permissions, AuditService audit)
    {
        _bookings = bookings; _settings = settings; _permissions = permissions; _audit = audit;
    }

    public async Task<PagedResult<Booking>> ListAsync(ActorContext actor, BookingFilter filter,
        PageRequest request, CancellationToken ct = default)
    {
        await _permissions.RequireAsync(actor, ScreenCodes.SalesBookings, ActionCodes.View, ct: ct);
        return await _bookings.ListAsync(filter, request, ct);
    }

    public async Task<Booking> GetAsync(ActorContext actor, Guid id, CancellationToken ct = default)
    {
        await _permissions.RequireAsync(actor, ScreenCodes.SalesBookings, ActionCodes.View, ct: ct);
        return await _bookings.GetAsync(id, ct) ?? throw new NotFoundAppException();
    }

    public async Task<Booking> CreateAsync(ActorContext actor, BookingInput input,
        CancellationToken ct = default)
    {
        await _permissions.RequireAsync(actor, ScreenCodes.SalesBookings, ActionCodes.Create, ct: ct);
        var id = Guid.CreateVersion7();
        try { await _bookings.CreateDraftAsync(id, input, actor.UserId, ct); }
        catch (PostgresException ex) { throw DbErrorMapper.Map(ex); }

        var booking = await _bookings.GetAsync(id, ct) ?? throw new NotFoundAppException();
        await _audit.WriteAsync(actor, "bookings", id.ToString(), "INSERT", null, booking, ct);
        return booking;
    }

    public async Task<Booking> UpdateAsync(ActorContext actor, Guid id, int version,
        BookingInput input, CancellationToken ct = default)
    {
        await _permissions.RequireAsync(actor, ScreenCodes.SalesBookings, ActionCodes.Edit, ct: ct);
        Booking booking;
        try { booking = await _bookings.UpdateDraftAsync(id, version, input, actor.UserId, ct); }
        catch (PostgresException ex) { throw DbErrorMapper.Map(ex); }
        await _audit.WriteAsync(actor, "bookings", id.ToString(), "UPDATE", null, booking, ct);
        return booking;
    }

    /// <summary>تأكيد الحجز: يرقّم المستند ويحجز المتاح حسب الإعداد.</summary>
    public async Task<Booking> ConfirmAsync(ActorContext actor, Guid id,
        CancellationToken ct = default)
    {
        await _permissions.RequireAsync(actor, ScreenCodes.SalesBookings, ActionCodes.Confirm, ct: ct);
        var settings = await _settings.GetAsync(ct);
        Booking booking;
        try { booking = await _bookings.ConfirmAsync(id, actor.UserId, settings.ReserveStockOnBooking, ct); }
        catch (PostgresException ex) { throw DbErrorMapper.Map(ex); }

        await _audit.WriteAsync(actor, "bookings", id.ToString(), "UPDATE",
            new { status = BookingStatuses.Draft }, new { booking.Status, booking.DocNo }, ct);
        return booking;
    }

    public async Task<Booking> CancelAsync(ActorContext actor, Guid id, string reason,
        CancellationToken ct = default)
    {
        await _permissions.RequireAsync(actor, ScreenCodes.SalesBookings, ActionCodes.CancelEntry,
            ct: ct);
        if (string.IsNullOrWhiteSpace(reason))
            throw new ValidationAppException("validation.cancel_reason_required");

        Booking booking;
        try { booking = await _bookings.CancelAsync(id, reason, actor.UserId, ct); }
        catch (PostgresException ex) { throw DbErrorMapper.Map(ex); }

        await _audit.WriteAsync(actor, "bookings", id.ToString(), "UPDATE",
            new { status = BookingStatuses.Confirmed }, new { status = booking.Status, reason }, ct);
        return booking;
    }

    public async Task<Booking> AssignAsync(ActorContext actor, Guid id, Guid employeeId,
        string role, CancellationToken ct = default)
    {
        await _permissions.RequireAsync(actor, ScreenCodes.SalesBookings,
            ActionCodes.AssignOthers, ct: ct);
        var booking = await _bookings.AssignEmployeeAsync(id, employeeId, role, actor.UserId, ct);
        await _audit.WriteAsync(actor, "booking_assignments", id.ToString(), "INSERT", null,
            new { employeeId, role }, ct);
        return booking;
    }

    public async Task<decimal> AvailableAsync(ActorContext actor, Guid itemId, Guid warehouseId,
        CancellationToken ct = default)
    {
        await _permissions.RequireAsync(actor, ScreenCodes.SalesBookings, ActionCodes.View, ct: ct);
        return await _bookings.AvailableAsync(itemId, warehouseId, ct);
    }
}
