using Npgsql;
using Ubp.Core.Models;
using Ubp.Core.Results;
using Ubp.Core.Security;
using Ubp.Infrastructure.Repositories;

namespace Ubp.Application.Services;

/// <summary>نقطة البيع: حجز منتجات + تحصيل اختياري في معاملة واحدة؛ لا مواعيد.</summary>
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

        // تاريخ نهاية الحجز: افتراضي 6 أشهر، ويُرفض التاريخ الماضي.
        input.ExpiresAt ??= DateTimeOffset.UtcNow.AddMonths(6);
        if (input.ExpiresAt.Value.UtcDateTime.Date < DateTimeOffset.UtcNow.Date)
            throw new ValidationAppException("validation.booking_expiry_invalid");

        var settings = await _settings.GetAsync(ct);
        BranchScope.Ensure(actor, input.BranchId);
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

    public async Task<IReadOnlyList<CommercialDocument>> ListReceiptsAsync(ActorContext actor,
        Guid? branchId, int limit = 100, CancellationToken ct = default)
    {
        await _permissions.RequireAsync(actor, ScreenCodes.PosReceipts, ActionCodes.View, ct: ct);
        return await _pos.ListReceiptsAsync(branchId, limit, ct);
    }
}
