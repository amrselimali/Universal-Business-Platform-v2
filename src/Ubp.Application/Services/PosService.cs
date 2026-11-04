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
    private readonly IReceiptNotifier _notifier;

    public PosService(IPosRepository pos, ISettingsService settings, PermissionService permissions,
        AuditService audit, IReceiptNotifier notifier)
    {
        _pos = pos; _settings = settings; _permissions = permissions; _audit = audit;
        _notifier = notifier;
    }

    public async Task<PosReceipt> CheckoutAsync(ActorContext actor, PosCheckoutInput input,
        CancellationToken ct = default)
    {
        await _permissions.RequireAsync(actor, ScreenCodes.PosTerminal, ActionCodes.Checkout,
            ct: ct);

        // تاريخ نهاية العرض: افتراضي 6 أشهر، ويُرفض التاريخ الماضي.
        input.ExpiresAt ??= DateTimeOffset.UtcNow.AddMonths(6);
        if (input.ExpiresAt.Value.UtcDateTime.Date < DateTimeOffset.UtcNow.Date)
            throw new ValidationAppException("validation.booking_expiry_invalid");

        var settings = await _settings.GetAsync(ct);
        BranchScope.Ensure(actor, input.BranchId);
        PosReceipt receipt;
        try
        {
            receipt = await _pos.CheckoutAsync(input, actor.UserId, settings.ReserveStockOnBooking,
                settings.AllowNegativeStock, ct);
        }
        catch (PostgresException ex) { throw DbErrorMapper.Map(ex); }

        await _audit.WriteAsync(actor, "bookings", receipt.BookingId.ToString(), "INSERT", null,
            new
            {
                receipt.BookingNo, receipt.ExpiresAt, receipt.Total, receipt.Paid,
                receipt.Executed,
                Vouchers = receipt.Vouchers.Select(v => v.VoucherNo).ToArray()
            }, ct);

        // إشعار العميل بالإيصال (واتساب) عند وجود مبلغ محصّل.
        if (receipt.Paid > 0)
            await _notifier.NotifyAsync(input.BranchId, input.CustomerId, receipt.BookingNo ?? "",
                receipt.Total + receipt.FeeTotal, receipt.Paid,
                Math.Max(receipt.Total + receipt.FeeTotal - receipt.Paid, 0), ct);

        return receipt;
    }

    public async Task<IReadOnlyList<CommercialDocument>> ListReceiptsAsync(ActorContext actor,
        Guid? branchId, int limit = 100, CancellationToken ct = default)
    {
        await _permissions.RequireAsync(actor, ScreenCodes.PosReceipts, ActionCodes.View, ct: ct);
        return await _pos.ListReceiptsAsync(branchId, limit, ct);
    }

    /// <summary>إعادة إرسال إيصال قبض إلى العميل عبر واتساب (من شاشة إيصالات POS).</summary>
    public async Task<bool> SendReceiptWhatsAppAsync(ActorContext actor, Guid voucherId,
        CancellationToken ct = default)
    {
        var allowed = await _permissions.HasAsync(actor, ScreenCodes.PosTerminal,
                          ActionCodes.Checkout, ct)
                      || await _permissions.HasAsync(actor, ScreenCodes.PosReceipts,
                          ActionCodes.View, ct);
        if (!allowed) throw new ForbiddenException();

        var head = await _pos.GetReceiptHeaderAsync(voucherId, ct);
        if (head.CustomerId is null || string.IsNullOrWhiteSpace(head.DocNo))
            throw new NotFoundAppException();
        BranchScope.Ensure(actor, head.BranchId);

        var sent = await _notifier.NotifyAsync(head.BranchId, head.CustomerId.Value, head.DocNo,
            head.Total, head.Total, null, ct);
        await _audit.WriteAsync(actor, "message_outbox", voucherId.ToString(), "INSERT", null,
            new { Channel = MessageChannels.WhatsApp, head.DocNo, Sent = sent }, ct);
        return sent;
    }

    /// <summary>تفاصيل إيصال قبض لعرضه من شاشة إيصالات POS.</summary>
    public async Task<Voucher> GetReceiptAsync(ActorContext actor, Guid voucherId,
        CancellationToken ct = default)
    {
        await _permissions.RequireAsync(actor, ScreenCodes.PosReceipts, ActionCodes.View, ct: ct);
        var voucher = await _pos.GetReceiptDetailAsync(voucherId, ct)
                      ?? throw new NotFoundAppException();
        BranchScope.Ensure(actor, voucher.BranchId);
        return voucher;
    }
}
