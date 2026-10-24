using Npgsql;
using Ubp.Core.Models;
using Ubp.Core.Results;
using Ubp.Core.Security;
using Ubp.Infrastructure.Repositories;

namespace Ubp.Application.Services;

/// <summary>نقطة البيع: حجز + تنفيذ + فاتورة + قبض في معاملة واحدة (4G).</summary>
public sealed class PosService
{
    private readonly IPosRepository _pos;
    private readonly ISettingsService _settings;
    private readonly PermissionService _permissions;
    private readonly AuditService _audit;

    public PosService(IPosRepository pos, ISettingsService settings, PermissionService permissions,
        AuditService audit)
    {
        _pos = pos; _settings = settings; _permissions = permissions; _audit = audit;
    }

    public async Task<PosReceipt> CheckoutAsync(ActorContext actor, PosCheckoutInput input,
        CancellationToken ct = default)
    {
        await _permissions.RequireAsync(actor, ScreenCodes.PosTerminal, ActionCodes.Checkout,
            ct: ct);
        var settings = await _settings.GetAsync(ct);
        PosReceipt receipt;
        try
        {
            receipt = await _pos.CheckoutAsync(input, actor.UserId, settings.ReserveStockOnBooking,
                ct);
        }
        catch (PostgresException ex) { throw DbErrorMapper.Map(ex); }

        await _audit.WriteAsync(actor, "bookings", receipt.BookingId.ToString(), "INSERT", null,
            new
            {
                receipt.BookingNo, receipt.Total, receipt.Paid,
                Vouchers = receipt.Vouchers.Select(v => v.VoucherNo).ToArray()
            }, ct);
        return receipt;
    }

    /// <summary>حجز موعد خدمة بوقت من/إلى دون أسعار أو كميات أو تحصيل.</summary>
    public async Task<PosReceipt> BookAppointmentAsync(ActorContext actor,
        PosAppointmentInput input, CancellationToken ct = default)
    {
        await _permissions.RequireAsync(actor, ScreenCodes.PosTerminal, ActionCodes.Checkout,
            ct: ct);
        PosReceipt receipt;
        try { receipt = await _pos.BookAppointmentAsync(input, actor.UserId, ct); }
        catch (PostgresException ex) { throw DbErrorMapper.Map(ex); }

        await _audit.WriteAsync(actor, "bookings", receipt.BookingId.ToString(), "INSERT", null,
            new { receipt.BookingNo, Appointment = true, input.StartsAt, input.EndsAt }, ct);
        return receipt;
    }

    public async Task<IReadOnlyList<CommercialDocument>> ListReceiptsAsync(ActorContext actor,
        Guid? branchId, int limit = 100, CancellationToken ct = default)
    {
        await _permissions.RequireAsync(actor, ScreenCodes.PosReceipts, ActionCodes.View, ct: ct);
        return await _pos.ListReceiptsAsync(branchId, limit, ct);
    }
}
