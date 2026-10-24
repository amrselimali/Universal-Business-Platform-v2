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
}
