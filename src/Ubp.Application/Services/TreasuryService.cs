using Npgsql;
using Ubp.Core.Models;
using Ubp.Core.Paging;
using Ubp.Core.Results;
using Ubp.Core.Security;
using Ubp.Infrastructure.Repositories;

namespace Ubp.Application.Services;

/// <summary>الخزائن وطرق الدفع والسندات والشفتات: صلاحيات + تحقق + تدقيق.</summary>
public sealed partial class TreasuryService
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
        Guid? branchId, bool includeArchived = false, CancellationToken ct = default)
    {
        await _permissions.RequireAsync(actor, ScreenCodes.Treasuries, ActionCodes.View, ct: ct);
        return await _treasury.ListTreasuriesAsync(branchId, includeArchived, ct);
    }

    public async Task<Treasury> CreateTreasuryAsync(ActorContext actor, TreasuryInput input,
        CancellationToken ct = default)
    {
        await _permissions.RequireAsync(actor, ScreenCodes.Treasuries, ActionCodes.Create, ct: ct);
        BranchScope.Ensure(actor, input.BranchId);
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

    /// <summary>طرق التحصيل المكوَّدة في خزائن الفرع، تُقرأ من شاشة POS بصلاحية البيع.</summary>
    public async Task<IReadOnlyList<TreasuryMethod>> ListBranchMethodsAsync(ActorContext actor,
        Guid branchId, CancellationToken ct = default)
    {
        await _permissions.RequireAsync(actor, ScreenCodes.PosTerminal, ActionCodes.View, ct: ct);
        BranchScope.Ensure(actor, branchId);
        return await _treasury.ListBranchMethodsAsync(branchId, ct);
    }

    public async Task<TreasuryMethod> CreateMethodAsync(ActorContext actor,
        TreasuryMethodInput input, CancellationToken ct = default)
    {
        await _permissions.RequireAsync(actor, ScreenCodes.Treasuries, ActionCodes.ManageMethods,
            ct: ct);
        await ValidateMethodAsync(input, ct);

        var id = Guid.CreateVersion7();
        try
        {
            await _treasury.CreateMethodAsync(id, input, actor.UserId, ct);
        }
        catch (PostgresException ex) { throw DbErrorMapper.Map(ex); }

        await _audit.WriteAsync(actor, "treasury_methods", id.ToString(), "INSERT", null, input, ct);
        return (await _treasury.ListMethodsAsync(input.TreasuryId, ct)).First(m => m.Id == id);
    }

    /// <summary>تعديل طريقة دفع مسجّلة (تظهر بها زر تعديل في شاشة الخزائن) — 0075.</summary>
    public async Task<TreasuryMethod> UpdateMethodAsync(ActorContext actor, Guid id,
        TreasuryMethodInput input, CancellationToken ct = default)
    {
        await _permissions.RequireAsync(actor, ScreenCodes.Treasuries, ActionCodes.ManageMethods,
            ct: ct);
        await ValidateMethodAsync(input, ct);

        try
        {
            if (!await _treasury.UpdateMethodAsync(id, input, ct))
                throw new NotFoundAppException();
        }
        catch (PostgresException ex) { throw DbErrorMapper.Map(ex); }

        await _audit.WriteAsync(actor, "treasury_methods", id.ToString(), "UPDATE", null, input, ct);
        return (await _treasury.ListMethodsAsync(input.TreasuryId, ct)).First(m => m.Id == id);
    }

    /// <summary>تفعيل/إيقاف «يتم متابعتها في الشفت» لطريقة دفع — 0075.</summary>
    public async Task SetMethodFollowInShiftAsync(ActorContext actor, Guid id, bool followInShift,
        CancellationToken ct = default)
    {
        await _permissions.RequireAsync(actor, ScreenCodes.Treasuries, ActionCodes.ManageMethods,
            ct: ct);
        if (!await _treasury.SetMethodFollowInShiftAsync(id, followInShift, ct))
            throw new NotFoundAppException();
        await _audit.WriteAsync(actor, "treasury_methods", id.ToString(), "UPDATE", null,
            new { follow_in_shift = followInShift }, ct);
    }

    private async Task ValidateMethodAsync(TreasuryMethodInput input, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(input.Code) || string.IsNullOrWhiteSpace(input.NameAr) ||
            input.TreasuryId == Guid.Empty)
            throw new ValidationAppException("validation.required_fields");
        if (!IsMethodType(input.MethodType))
            throw new ValidationAppException("validation.method_type");
        if (input.MethodType != PaymentMethodTypes.Cash && input.ClearingAccountId is null)
            throw new ValidationAppException("validation.clearing_account_required");
        if (input.FeePercent is < 0 or > 100 || input.FeeFixed < 0)
            throw new ValidationAppException("validation.payment_fee");
        if (input.FeeAccountId is { } feeAccountId)
        {
            var postable = await _accounts.ListPostableAsync(ct);
            if (postable.All(a => a.Id != feeAccountId))
                throw new ValidationAppException("validation.account_not_postable");
        }
    }

    /// <summary>حساب زيادة/عجز الخزينة عند اعتماد المحاسب (بديل الافتراضي العام) — 0076.</summary>
    public async Task SetTreasuryOverShortAccountAsync(ActorContext actor, Guid treasuryId,
        Guid? accountId, CancellationToken ct = default)
    {
        await _permissions.RequireAsync(actor, ScreenCodes.Treasuries, ActionCodes.ManageMethods,
            ct: ct);
        if (accountId is { } id)
        {
            var postable = await _accounts.ListPostableAsync(ct);
            if (postable.All(a => a.Id != id))
                throw new ValidationAppException("validation.account_not_postable");
        }
        if (!await _treasury.SetTreasuryOverShortAccountAsync(treasuryId, accountId, ct))
            throw new NotFoundAppException();
        await _audit.WriteAsync(actor, "treasuries", treasuryId.ToString(), "UPDATE", null,
            new { over_short_account_id = accountId }, ct);
    }

    /// <summary>نسبة عمولة التحصيل وحساب ترحيلها (0081).</summary>
    public async Task SetCollectionFeeAsync(ActorContext actor, Guid treasuryId, decimal percent,
        Guid? accountId, CancellationToken ct = default)
    {
        await _permissions.RequireAsync(actor, ScreenCodes.Treasuries, ActionCodes.ManageMethods,
            ct: ct);
        if (percent is < 0 or > 100)
            throw new ValidationAppException("validation.payment_fee");
        if (accountId is { } id)
        {
            var postable = await _accounts.ListPostableAsync(ct);
            if (postable.All(a => a.Id != id))
                throw new ValidationAppException("validation.account_not_postable");
        }
        if (!await _treasury.SetCollectionFeeAsync(treasuryId, percent, accountId, ct))
            throw new NotFoundAppException();
        await _audit.WriteAsync(actor, "treasuries", treasuryId.ToString(), "UPDATE", null,
            new { collection_fee_percent = percent, collection_fee_account_id = accountId }, ct);
    }

    /// <summary>تسليم نقدية من خزينة إلى حساب وسيط لنقدية الفرع (0080).</summary>
    public async Task<Guid> CreateCashHandoverAsync(ActorContext actor, CashHandoverInput input,
        CancellationToken ct = default)
    {
        await _permissions.RequireAsync(actor, ScreenCodes.OperatingExpenses, ActionCodes.Create,
            ct: ct);
        BranchScope.Ensure(actor, input.BranchId);
        var id = Guid.CreateVersion7();
        try { await _treasury.CreateCashHandoverAsync(id, input, actor.UserId, ct); }
        catch (PostgresException ex) { throw DbErrorMapper.Map(ex); }
        await _audit.WriteAsync(actor, "cash_handovers", id.ToString(), "INSERT", null, input, ct);
        return id;
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
}
