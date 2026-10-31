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
    private readonly ITreasuryRepository _treasuries;
    private readonly IPartnerRepository _partners;
    private readonly TreasuryService _treasury;
    private readonly BookingService _bookings;
    private readonly PermissionService _permissions;

    public ReservationService(IReservationRepository reservations, ITreasuryRepository treasuries,
        IPartnerRepository partners, TreasuryService treasury, BookingService bookings,
        PermissionService permissions)
    {
        _reservations = reservations; _treasuries = treasuries; _partners = partners;
        _treasury = treasury; _bookings = bookings; _permissions = permissions;
    }

    public async Task<PagedResult<ReservationRow>> ListAsync(ActorContext actor, PageRequest request,
        Guid? customerId = null, bool onlyRemaining = false, CancellationToken ct = default)
    {
        await _permissions.RequireAsync(actor, ScreenCodes.Reservations, ActionCodes.View, ct: ct);
        // المدير العام يرى الكل، وغيره يرى فروعه فقط (القص في الخدمة لا في الواجهة).
        var branchIds = actor.IsSuperAdmin
            ? Array.Empty<Guid>()
            : actor.AllowedBranchIds.Distinct().ToArray();
        return await _reservations.ListAsync(request, branchIds, customerId, onlyRemaining, ct);
    }

    /// <summary>يحوّل المتبقي غير المستخدم إلى دفعة مقدمة (سند قبض) مرة واحدة فقط لكل حجز.</summary>
    public async Task<ReservationConversion> ConvertToAdvanceAsync(ActorContext actor, Guid bookingId,
        CancellationToken ct = default)
    {
        await _permissions.RequireAsync(actor, ScreenCodes.Reservations, ActionCodes.ConvertToAdvance,
            ct: ct);

        var lines = await _reservations.RemainingLinesAsync(bookingId, ct);
        if (lines.Count == 0) throw new ValidationAppException("validation.nothing_to_convert");

        var header = lines[0];
        BranchScope.Ensure(actor, header.BranchId);
        if (await _reservations.AdvanceExistsAsync(bookingId, ct))
            throw new ConflictException("validation.advance_already_converted");

        var amount = Math.Round(lines.Sum(l => l.RemainingValue), 2, MidpointRounding.AwayFromZero);
        var quantity = lines.Sum(l => l.RemainingQty);
        if (amount <= 0) throw new ValidationAppException("validation.nothing_to_convert");

        var treasury = (await _treasuries.ListTreasuriesAsync(header.BranchId, ct))
            .Where(t => t.IsActive)
            .OrderBy(t => string.Equals(t.TreasuryType, TreasuryTypes.Cash,
                StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .FirstOrDefault() ?? throw new ValidationAppException("validation.treasury_required");
        var method = (await _treasuries.ListMethodsAsync(treasury.Id, ct)).FirstOrDefault()
            ?? throw new ValidationAppException("validation.payment_method_required");
        var customer = await _partners.GetAsync(PartnerKind.Customer, header.CustomerId, ct)
            ?? throw new NotFoundAppException();

        var draft = await _treasury.CreateVoucherAsync(actor, new VoucherInput
        {
            VoucherType = VoucherTypes.Receipt,
            BranchId = header.BranchId,
            DocDate = DateTime.Today,
            PartyType = "C",
            PartyId = header.CustomerId,
            PartyName = customer.NameAr,
            TreasuryId = treasury.Id,
            Notes = $"تحويل متبقي حجز {header.BookingNo} إلى دفعة مقدمة",
            Lines = { new VoucherLineInput { TreasuryMethodId = method.Id, Amount = amount } }
        }, ct);

        var posted = await _treasury.PostVoucherAsync(actor, draft.Id, ct);

        var converted = 0;
        foreach (var line in lines)
        {
            var id = await _reservations.InsertAdjustmentAsync(bookingId, line.BookingLineId,
                ReservationAdjustmentKinds.ToAdvance, line.RemainingQty, line.RemainingValue,
                posted.Id, posted.JournalEntryId, null, actor.UserId, ct);
            if (id is not null) converted++;
        }

        return new ReservationConversion(posted.Id, amount, quantity, converted);
    }
}
