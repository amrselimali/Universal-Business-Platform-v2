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
    public async Task<IReadOnlyList<Voucher>> ListVouchersAsync(ActorContext actor, Guid? branchId,
        string? voucherType, string? status, int limit = 100, CancellationToken ct = default)
    {
        await _permissions.RequireAsync(actor, ScreenCodes.TreasuryVouchers, ActionCodes.View, ct: ct);
        return await _treasury.ListVouchersAsync(branchId, voucherType, status, limit, ct);
    }

    public async Task<Voucher> GetVoucherAsync(ActorContext actor, Guid id,
        CancellationToken ct = default)
    {
        await _permissions.RequireAsync(actor, ScreenCodes.TreasuryVouchers, ActionCodes.View, ct: ct);
        return await _treasury.GetVoucherAsync(id, ct) ?? throw new NotFoundAppException();
    }

    public async Task<Voucher> CreateVoucherAsync(ActorContext actor, VoucherInput input,
        CancellationToken ct = default)
    {
        await _permissions.RequireAsync(actor, ScreenCodes.TreasuryVouchers, ActionCodes.Create,
            ct: ct);
        BranchScope.Ensure(actor, input.BranchId);
        var id = Guid.CreateVersion7();
        try
        {
            await _treasury.CreateVoucherAsync(id, input, actor.UserId, ct);
        }
        catch (PostgresException ex) { throw DbErrorMapper.Map(ex); }

        var voucher = await _treasury.GetVoucherAsync(id, ct) ?? throw new NotFoundAppException();
        await _audit.WriteAsync(actor, "vouchers", id.ToString(), "INSERT", null, voucher, ct);
        return voucher;
    }

    public async Task<Voucher> PostVoucherAsync(ActorContext actor, Guid id,
        CancellationToken ct = default)
    {
        var existing = await _treasury.GetVoucherAsync(id, ct) ?? throw new NotFoundAppException();
        await _permissions.RequireAsync(actor, ScreenCodes.TreasuryVouchers, ActionCodes.Post,
            ct: ct);
        if (!existing.IsDraft)
            throw new ValidationAppException("validation.voucher_not_draft");

        try
        {
            await _treasury.PostVoucherAsync(id, actor.UserId, ct);
        }
        catch (PostgresException ex) { throw DbErrorMapper.Map(ex); }

        var posted = await _treasury.GetVoucherAsync(id, ct) ?? throw new NotFoundAppException();
        await _audit.WriteAsync(actor, "vouchers", id.ToString(), "UPDATE",
            new { status = existing.Status }, new { status = posted.Status, posted.DocNo }, ct);
        return posted;
    }

    public async Task<MutationOutcome> DeleteVoucherAsync(ActorContext actor, Guid id,
        CancellationToken ct = default)
    {
        var existing = await _treasury.GetVoucherAsync(id, ct) ?? throw new NotFoundAppException();
        await _permissions.RequireAsync(actor, ScreenCodes.TreasuryVouchers, ActionCodes.Delete,
            ct: ct);
        if (!existing.IsDraft)
            throw new ValidationAppException("validation.voucher_not_draft");

        try
        {
            if (!await _treasury.DeleteVoucherAsync(id, ct)) throw new NotFoundAppException();
        }
        catch (PostgresException ex) { throw DbErrorMapper.Map(ex); }

        await _audit.WriteAsync(actor, "vouchers", id.ToString(), "DELETE", existing, null, ct);
        return new MutationOutcome(true, false, "common.deleted");
    }

    /// <summary>تخصيص دفعة على فاتورة مرحّلة (تسوية/مقاصة).</summary>
    public async Task<Guid> AllocateAsync(ActorContext actor, Guid voucherId, Guid documentId,
        decimal amount, CancellationToken ct = default)
    {
        await _permissions.RequireAsync(actor, ScreenCodes.TreasuryVouchers, ActionCodes.Edit,
            ct: ct);
        Guid allocationId;
        try
        {
            allocationId = await _treasury.AllocateAsync(voucherId, documentId, amount,
                actor.UserId, ct);
        }
        catch (PostgresException ex) { throw DbErrorMapper.Map(ex); }

        await _audit.WriteAsync(actor, "document_allocations", allocationId.ToString(), "INSERT",
            null, new { voucherId, documentId, amount }, ct);
        return allocationId;
    }

    public async Task<IReadOnlyList<VoucherAllocation>> ListAllocationsAsync(ActorContext actor,
        Guid voucherId, CancellationToken ct = default)
    {
        await _permissions.RequireAsync(actor, ScreenCodes.TreasuryVouchers, ActionCodes.View,
            ct: ct);
        return await _treasury.ListAllocationsAsync(voucherId, ct);
    }

    /// <summary>إلغاء مسودة سند (لا يُلغى المرحّل — يحتاج قيد عكس).</summary>
    public async Task<MutationOutcome> CancelVoucherAsync(ActorContext actor, Guid id,
        CancellationToken ct = default)
    {
        var existing = await _treasury.GetVoucherAsync(id, ct) ?? throw new NotFoundAppException();
        await _permissions.RequireAsync(actor, ScreenCodes.TreasuryVouchers,
            ActionCodes.CancelEntry, ct: ct);
        if (!existing.IsDraft)
            throw new ValidationAppException("validation.voucher_not_draft");

        try
        {
            if (!await _treasury.CancelVoucherAsync(id, ct)) throw new NotFoundAppException();
        }
        catch (PostgresException ex) { throw DbErrorMapper.Map(ex); }

        await _audit.WriteAsync(actor, "vouchers", id.ToString(), "UPDATE",
            new { status = existing.Status }, new { status = VoucherStatuses.Cancelled }, ct);
        return new MutationOutcome(false, false, "treasury.voucher_cancelled");
    }

    /// <summary>فواتير الطرف المفتوحة لتخصيص السند عليها.</summary>
    public async Task<IReadOnlyList<OpenDocument>> ListOpenDocumentsAsync(ActorContext actor,
        Guid voucherId, CancellationToken ct = default)
    {
        await _permissions.RequireAsync(actor, ScreenCodes.TreasuryVouchers, ActionCodes.View,
            ct: ct);
        var voucher = await _treasury.GetVoucherAsync(voucherId, ct)
                      ?? throw new NotFoundAppException();
        if (voucher.PartyId is null || string.IsNullOrWhiteSpace(voucher.PartyType))
            return Array.Empty<OpenDocument>();

        return await _treasury.ListOpenDocumentsAsync(voucher.PartyType, voucher.PartyId.Value,
            200, ct);
    }
}
