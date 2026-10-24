using Npgsql;
using Ubp.Core.Models;
using Ubp.Core.Paging;
using Ubp.Core.Results;
using Ubp.Core.Security;
using Ubp.Infrastructure.Repositories;

namespace Ubp.Application.Services;

/// <summary>الخزائن وطرق الدفع والسندات والورديات: صلاحيات + تحقق + تدقيق.</summary>
public sealed class TreasuryService
{
    private readonly ITreasuryRepository _treasury;
    private readonly IAccountRepository _accounts;
    private readonly IPartnerRepository _partners;
    private readonly PermissionService _permissions;
    private readonly AuditService _audit;

    public TreasuryService(ITreasuryRepository treasury, IAccountRepository accounts,
        IPartnerRepository partners, PermissionService permissions, AuditService audit)
    {
        _treasury = treasury; _accounts = accounts; _partners = partners;
        _permissions = permissions; _audit = audit;
    }

    private static bool IsTreasuryType(string value) =>
        value is TreasuryTypes.Cash or TreasuryTypes.Bank or TreasuryTypes.Wallet;

    private static bool IsMethodType(string value) =>
        value is PaymentMethodTypes.Cash or PaymentMethodTypes.Card
            or PaymentMethodTypes.Wallet or PaymentMethodTypes.Cheque
            or PaymentMethodTypes.Transfer;

    public async Task<IReadOnlyList<Treasury>> ListTreasuriesAsync(ActorContext actor,
        Guid? branchId, CancellationToken ct = default)
    {
        await _permissions.RequireAsync(actor, ScreenCodes.Treasuries, ActionCodes.View, ct: ct);
        return await _treasury.ListTreasuriesAsync(branchId, ct);
    }

    public async Task<Treasury> CreateTreasuryAsync(ActorContext actor, TreasuryInput input,
        CancellationToken ct = default)
    {
        await _permissions.RequireAsync(actor, ScreenCodes.Treasuries, ActionCodes.Create, ct: ct);
        if (string.IsNullOrWhiteSpace(input.Code) || string.IsNullOrWhiteSpace(input.NameAr) ||
            input.AccountId == Guid.Empty)
            throw new ValidationAppException("validation.required_fields");
        if (!IsTreasuryType(input.TreasuryType))
            throw new ValidationAppException("validation.treasury_type");
        if (input.OpeningBalance < 0)
            throw new ValidationAppException("validation.opening_balance");

        var id = Guid.CreateVersion7();
        try
        {
            await _treasury.CreateTreasuryAsync(id, input, actor.UserId, ct);
        }
        catch (PostgresException ex) { throw DbErrorMapper.Map(ex); }

        await _audit.WriteAsync(actor, "treasuries", id.ToString(), "INSERT", null, input, ct);
        return (await _treasury.ListTreasuriesAsync(input.BranchId, ct)).First(t => t.Id == id);
    }

    public async Task<IReadOnlyList<TreasuryMethod>> ListMethodsAsync(ActorContext actor,
        Guid treasuryId, CancellationToken ct = default)
    {
        await _permissions.RequireAsync(actor, ScreenCodes.Treasuries, ActionCodes.View, ct: ct);
        return await _treasury.ListMethodsAsync(treasuryId, ct);
    }

    public async Task<TreasuryMethod> CreateMethodAsync(ActorContext actor,
        TreasuryMethodInput input, CancellationToken ct = default)
    {
        await _permissions.RequireAsync(actor, ScreenCodes.Treasuries, ActionCodes.ManageMethods,
            ct: ct);
        if (string.IsNullOrWhiteSpace(input.Code) || string.IsNullOrWhiteSpace(input.NameAr) ||
            input.TreasuryId == Guid.Empty)
            throw new ValidationAppException("validation.required_fields");
        if (!IsMethodType(input.MethodType))
            throw new ValidationAppException("validation.method_type");
        if (input.MethodType != PaymentMethodTypes.Cash && input.ClearingAccountId is null)
            throw new ValidationAppException("validation.clearing_account_required");
        if (input.FeePercent is < 0 or > 100 || input.FeeFixed < 0)
            throw new ValidationAppException("validation.payment_fee");

        var id = Guid.CreateVersion7();
        try
        {
            await _treasury.CreateMethodAsync(id, input, actor.UserId, ct);
        }
        catch (PostgresException ex) { throw DbErrorMapper.Map(ex); }

        await _audit.WriteAsync(actor, "treasury_methods", id.ToString(), "INSERT", null, input, ct);
        return (await _treasury.ListMethodsAsync(input.TreasuryId, ct)).First(m => m.Id == id);
    }

    /// <summary>حسابات الترحيل المتاحة لشاشات الخزينة (بدون اشتراط صلاحية شاشة الحسابات).</summary>
    public async Task<IReadOnlyList<Account>> PostableAccountsAsync(ActorContext actor,
        CancellationToken ct = default)
    {
        await RequireAnyTreasuryViewAsync(actor, ct);
        return await _accounts.ListPostableAsync(ct);
    }

    /// <summary>عملاء أو موردون لقائمة اختيار الطرف في السند (C/S).</summary>
    public async Task<IReadOnlyList<Partner>> ListPartiesAsync(ActorContext actor, string partyType,
        CancellationToken ct = default)
    {
        await RequireAnyTreasuryViewAsync(actor, ct);
        var kind = partyType == "S" ? PartnerKind.Supplier : PartnerKind.Customer;
        var page = await _partners.ListAsync(kind, new PageRequest { Page = 1, PageSize = 200 },
            null, actor.IsAdmin, actor.AllowedBranchIds, ct);
        return page.Items;
    }

    private async Task RequireAnyTreasuryViewAsync(ActorContext actor, CancellationToken ct)
    {
        var allowed = await _permissions.HasAsync(actor, ScreenCodes.Treasuries, ActionCodes.View, ct)
                   || await _permissions.HasAsync(actor, ScreenCodes.TreasuryVouchers,
                       ActionCodes.View, ct)
                   || await _permissions.HasAsync(actor, ScreenCodes.TreasuryShifts,
                       ActionCodes.View, ct);
        if (!allowed) throw new ForbiddenException();
    }
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

    public async Task<IReadOnlyList<Shift>> ListShiftsAsync(ActorContext actor, Guid? branchId,
        int limit = 100, CancellationToken ct = default)
    {
        await _permissions.RequireAsync(actor, ScreenCodes.TreasuryShifts, ActionCodes.View, ct: ct);
        return await _treasury.ListShiftsAsync(branchId, limit, ct);
    }

    public async Task<Shift?> GetOpenShiftAsync(ActorContext actor, Guid treasuryId,
        CancellationToken ct = default)
    {
        await _permissions.RequireAsync(actor, ScreenCodes.TreasuryShifts, ActionCodes.View, ct: ct);
        return await _treasury.GetOpenShiftAsync(treasuryId, ct);
    }

    public async Task<Shift> OpenShiftAsync(ActorContext actor, ShiftInput input,
        CancellationToken ct = default)
    {
        await _permissions.RequireAsync(actor, ScreenCodes.TreasuryShifts, ActionCodes.Open, ct: ct);
        var id = Guid.CreateVersion7();
        try
        {
            await _treasury.OpenShiftAsync(id, input, actor.UserId, ct);
        }
        catch (PostgresException ex) { throw DbErrorMapper.Map(ex); }

        var shift = await _treasury.GetShiftAsync(id, ct) ?? throw new NotFoundAppException();
        await _audit.WriteAsync(actor, "cashier_shifts", id.ToString(), "INSERT", null, shift, ct);
        return shift;
    }

    /// <summary>إغلاق وردية: الفرق (زيادة/عجز) يُرحّل ويُدوَّن في التدقيق.</summary>
    public async Task<Shift> CloseShiftAsync(ActorContext actor, Guid shiftId, decimal countedCash,
        CancellationToken ct = default)
    {
        await _permissions.RequireAsync(actor, ScreenCodes.TreasuryShifts, ActionCodes.Close, ct: ct);
        Shift shift;
        try
        {
            shift = await _treasury.CloseShiftAsync(shiftId, countedCash, actor.UserId, ct);
        }
        catch (PostgresException ex) { throw DbErrorMapper.Map(ex); }

        await _audit.WriteAsync(actor, "cashier_shifts", shiftId.ToString(), "UPDATE",
            null, new { shift.Status, shift.ExpectedCash, shift.CountedCash, shift.Variance }, ct);
        return shift;
    }

    public async Task SaveDenominationsAsync(ActorContext actor, Guid shiftId,
        IReadOnlyList<ShiftDenominationInput> denominations, CancellationToken ct = default)
    {
        await _permissions.RequireAsync(actor, ScreenCodes.TreasuryShifts, ActionCodes.Open, ct: ct);
        await _treasury.SaveDenominationsAsync(shiftId, denominations, ct);
        await _audit.WriteAsync(actor, "shift_denominations", shiftId.ToString(), "UPDATE", null,
            denominations, ct);
    }

    public async Task<IReadOnlyList<ShiftDenomination>> ListDenominationsAsync(ActorContext actor,
        Guid shiftId, CancellationToken ct = default)
    {
        await _permissions.RequireAsync(actor, ScreenCodes.TreasuryShifts, ActionCodes.View, ct: ct);
        return await _treasury.ListDenominationsAsync(shiftId, ct);
    }

}
