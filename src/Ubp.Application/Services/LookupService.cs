using Ubp.Core.Models;
using Ubp.Core.Paging;
using Ubp.Core.Security;
using Ubp.Infrastructure.Repositories;

namespace Ubp.Application.Services;

/// <summary>
/// خدمات بحث خفيفة لقوائم الاختيار (طرف/صنف/مخزن/تصنيف).
/// تُغني الصفحات عن حقن المستودعات مباشرة، وتطبّق نطاق الفرع من ActorContext.
/// </summary>
public sealed class LookupService
{
    private readonly IPartnerRepository _partners;
    private readonly IItemRepository _items;
    private readonly IWarehouseRepository _warehouses;
    private readonly IItemCategoryRepository _categories;

    public LookupService(IPartnerRepository partners, IItemRepository items,
        IWarehouseRepository warehouses, IItemCategoryRepository categories)
    {
        _partners = partners; _items = items; _warehouses = warehouses; _categories = categories;
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

    /// <summary>مخازن ضمن صلاحيات المستخدم ونطاق فروعه.</summary>
    public Task<IReadOnlyList<Warehouse>> WarehousesAsync(ActorContext actor,
        CancellationToken ct = default)
    {
        EnsureActor(actor);
        return _warehouses.ListOptionsAsync(PermissionScope.All, actor.UserId,
            actor.AllowedBranchIds, actor.IsAdmin, ct);
    }

    /// <summary>تصنيفات الأصناف للاختيار.</summary>
    public Task<IReadOnlyList<ItemCategory>> ItemCategoriesAsync(ActorContext actor,
        CancellationToken ct = default)
    {
        EnsureActor(actor);
        return _categories.ListOptionsAsync(ct);
    }

    private static void EnsureActor(ActorContext actor)
    {
        if (actor.UserId == Guid.Empty)
            throw new UnauthorizedAccessException("SESSION_REQUIRED");
    }
}
