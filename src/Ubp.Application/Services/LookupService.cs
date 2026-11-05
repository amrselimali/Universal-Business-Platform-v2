using Ubp.Core.Models;
using Ubp.Core.Paging;
using Ubp.Core.Security;
using Ubp.Infrastructure.Repositories;

namespace Ubp.Application.Services;

/// <summary>
/// خدمات بحث خفيفة لقوائم الاختيار (طرف/صنف/مخزن/خزينة/طريقة دفع/حساب/مركز تكلفة).
/// تُغني الصفحات عن حقن المستودعات مباشرة، ولا تشترط صلاحية شاشة أخرى، وتطبّق نطاق
/// الفرع والمخزن المصرح به للمستخدم من ActorContext (إصلاح ربط الشاشات باليوزر).
/// </summary>
public sealed class LookupService
{
    private readonly IPartnerRepository _partners;
    private readonly IItemRepository _items;
    private readonly IWarehouseRepository _warehouses;
    private readonly IItemCategoryRepository _categories;
    private readonly ITreasuryRepository _treasuries;
    private readonly IAccountRepository _accounts;
    private readonly ICostCenterRepository _costCenters;

    public LookupService(IPartnerRepository partners, IItemRepository items,
        IWarehouseRepository warehouses, IItemCategoryRepository categories,
        ITreasuryRepository treasuries, IAccountRepository accounts,
        ICostCenterRepository costCenters)
    {
        _partners = partners; _items = items; _warehouses = warehouses;
        _categories = categories; _treasuries = treasuries; _accounts = accounts;
        _costCenters = costCenters;
    }

    /// <summary>أطراف نشِطة ضمن نطاق فرع المستخدم (المدير العام يرى الكل).</summary>
    public async Task<IReadOnlyList<Partner>> PartnersAsync(ActorContext actor, PartnerKind kind,
        string? search = null, CancellationToken ct = default)
    {
        EnsureActor(actor);
        var page = await _partners.ListAsync(kind, new PageRequest
        {
            Page = 1, PageSize = PageRequest.MaxPageSize, Search = search
        }, null, actor.IsSuperAdmin, actor.AllowedBranchIds, ct);
        return page.Items;
    }

    /// <summary>أصناف للاختيار (حد أقصى آمن داخل المستودع).</summary>
    public Task<IReadOnlyList<ItemOption>> ItemsAsync(ActorContext actor, string? search,
        int take = 50, bool sellableOnly = false, CancellationToken ct = default)
    {
        EnsureActor(actor);
        return _items.ListOptionsAsync(search, take, sellableOnly, ct);
    }

    /// <summary>مخازن المستخدم المصرح بها (user_warehouses) أو مخازن فروعه عند عدم الربط.</summary>
    public Task<IReadOnlyList<Warehouse>> WarehousesAsync(ActorContext actor,
        CancellationToken ct = default)
    {
        EnsureActor(actor);
        return _warehouses.ListOptionsForUserAsync(actor.UserId, actor.AllowedBranchIds,
            actor.IsSuperAdmin, ct);
    }

    /// <summary>تصنيفات الأصناف للاختيار.</summary>
    public Task<IReadOnlyList<ItemCategory>> ItemCategoriesAsync(ActorContext actor,
        CancellationToken ct = default)
    {
        EnsureActor(actor);
        return _categories.ListOptionsAsync(ct);
    }

    /// <summary>خزائن الفرع المصرح به فقط (بدون اشتراط صلاحية شاشة الخزائن).</summary>
    public async Task<IReadOnlyList<Treasury>> TreasuriesAsync(ActorContext actor,
        Guid? branchId, CancellationToken ct = default)
    {
        EnsureActor(actor);
        return await _treasuries.ListTreasuriesAsync(ScopedBranch(actor, branchId), ct);
    }

    /// <summary>طرق الدفع في خزائن الفرع المصرح به (بدون اشتراط صلاحية شاشة الخزائن).</summary>
    public async Task<IReadOnlyList<TreasuryMethod>> TreasuryMethodsAsync(ActorContext actor,
        Guid branchId, CancellationToken ct = default)
    {
        EnsureActor(actor);
        BranchScope.Ensure(actor, branchId);
        return await _treasuries.ListBranchMethodsAsync(branchId, ct);
    }

    /// <summary>حسابات الترحيل (لكل شاشة تحتاج اختيار حساب: مصروفات/سندات/قيود).</summary>
    public async Task<IReadOnlyList<Account>> PostableAccountsAsync(ActorContext actor,
        CancellationToken ct = default)
    {
        EnsureActor(actor);
        return await _accounts.ListPostableAsync(ct);
    }

    /// <summary>حسابات «وسيط لنقدية الفرع» لتسليم النقدية — 0080.</summary>
    public async Task<IReadOnlyList<Account>> CashClearingAccountsAsync(ActorContext actor,
        CancellationToken ct = default)
    {
        EnsureActor(actor);
        return await _accounts.ListCashClearingAsync(ct);
    }

    /// <summary>مراكز التكلفة داخل الفرع المصرح به.</summary>
    public async Task<IReadOnlyList<CostCenter>> CostCentersAsync(ActorContext actor,
        Guid? branchId, CancellationToken ct = default)
    {
        EnsureActor(actor);
        return await _costCenters.ListOptionsAsync(ScopedBranch(actor, branchId), ct);
    }

    private static Guid? ScopedBranch(ActorContext actor, Guid? branchId)
    {
        if (branchId is { } value)
        {
            BranchScope.Ensure(actor, value);
            return value;
        }
        if (actor.IsSuperAdmin) return null;
        if (actor.CurrentBranchId is { } current) return current;
        return actor.AllowedBranchIds.Count > 0 ? actor.AllowedBranchIds.First() : Guid.Empty;
    }

    private static void EnsureActor(ActorContext actor)
    {
        if (actor.UserId == Guid.Empty)
            throw new UnauthorizedAccessException("SESSION_REQUIRED");
    }
}

