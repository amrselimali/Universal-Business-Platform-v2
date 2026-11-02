using Ubp.Core.Models;
using Ubp.Core.Paging;
using Ubp.Core.Results;
using Ubp.Core.Security;
using Ubp.Infrastructure.Repositories;

namespace Ubp.Application.Services;

/// <summary>
/// الحجوزات كدفعات مقدمة (8ب): يعرض المحجوز/المستهلك/المتبقي لكل عميل، ويحوّل المتبقي غير المستخدم
/// إلى **دفعة مقدمة** (سند قبض مرحَّل)، ثم تستهلكها الفاتورة/الجلسة وتُحمّل الباقي على حساب العميل.
/// </summary>
public sealed partial class ReservationService
{
    private readonly IReservationRepository _reservations;
    private readonly BookingService _bookings;
    private readonly PermissionService _permissions;

    public ReservationService(IReservationRepository reservations, BookingService bookings,
        PermissionService permissions)
    {
        _reservations = reservations; _bookings = bookings; _permissions = permissions;
    }

    public async Task<PagedResult<ReservationRow>> ListAsync(ActorContext actor, PageRequest request,
        Guid? customerId = null, bool onlyRemaining = false, string? customerSearch = null,
        CancellationToken ct = default)
    {
        await _permissions.RequireAsync(actor, ScreenCodes.Reservations, ActionCodes.View, ct: ct);
        // المدير العام يرى الكل، وغيره يرى فروعه فقط (القص في الخدمة لا في الواجهة).
        var branchIds = actor.IsSuperAdmin
            ? Array.Empty<Guid>()
            : actor.AllowedBranchIds.Distinct().ToArray();
        return await _reservations.ListAsync(request, branchIds, customerId, onlyRemaining,
            customerSearch, ct);
    }

}
