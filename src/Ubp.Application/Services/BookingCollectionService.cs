using Npgsql;
using Ubp.Core.Models;
using Ubp.Core.Results;
using Ubp.Core.Security;
using Ubp.Infrastructure.Repositories;

namespace Ubp.Application.Services;

/// <summary>
/// تحصيل قيمة الخدمات المحجوزة من نقطة البيع: يقرأ أرصدة الحجوزات (الإجمالي/المسدد/المستحق)
/// ويُنشئ سند قبض مرحّلًا يخصّص المبالغ على الحجوزات، مع تدقيق ونطاق فرع.
/// </summary>
public sealed class BookingCollectionService
{
    private readonly IBookingCollectionRepository _collections;
    private readonly PermissionService _permissions;
    private readonly AuditService _audit;
    private readonly IReceiptNotifier _notifier;

    public BookingCollectionService(IBookingCollectionRepository collections,
        PermissionService permissions, AuditService audit, IReceiptNotifier notifier)
    {
        _collections = collections; _permissions = permissions; _audit = audit;
        _notifier = notifier;
    }

    public async Task<IReadOnlyList<BookingCollectionRow>> ListOutstandingAsync(
        ActorContext actor, Guid branchId, Guid customerId, CancellationToken ct = default)
    {
        await _permissions.RequireAsync(actor, ScreenCodes.PosTerminal, ActionCodes.Collect,
            ct: ct);
        BranchScope.Ensure(actor, branchId);
        return await _collections.ListOutstandingAsync(branchId, customerId, ct);
    }

    public async Task<BookingCollectionResult> CollectAsync(ActorContext actor,
        BookingCollectionInput input, CancellationToken ct = default)
    {
        await _permissions.RequireAsync(actor, ScreenCodes.PosTerminal, ActionCodes.Collect,
            ct: ct);
        BranchScope.Ensure(actor, input.BranchId);
        var id = Guid.CreateVersion7();
        BookingCollectionResult result;
        try { result = await _collections.CollectAsync(id, input, actor.UserId, ct); }
        catch (PostgresException ex) { throw DbErrorMapper.Map(ex); }

        await _audit.WriteAsync(actor, "booking_payments", id.ToString(), "INSERT", null,
            new
            {
                input.CustomerId, result.VoucherNo, result.Total, result.Fee,
                Lines = input.Lines.Select(l => new { l.BookingId, l.Amount }).ToArray()
            }, ct);

        if (!input.DraftOnly)
            await _notifier.NotifyAsync(input.BranchId, input.CustomerId,
                result.VoucherNo ?? "", result.Total, result.Total, null, ct);
        return result;
    }
}
