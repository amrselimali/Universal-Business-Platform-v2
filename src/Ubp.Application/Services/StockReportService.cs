using Ubp.Core.Models;
using Ubp.Core.Paging;
using Ubp.Core.Results;
using Ubp.Core.Security;
using Ubp.Infrastructure.Repositories;

namespace Ubp.Application.Services;

public sealed class StockReportService
{
    private readonly IStockReportRepository _reports;
    private readonly IItemRepository _items;
    private readonly IWarehouseRepository _warehouses;
    private readonly PermissionService _permissions;

    public StockReportService(IStockReportRepository reports, IItemRepository items,
        IWarehouseRepository warehouses, PermissionService permissions)
    {
        _reports = reports; _items = items; _warehouses = warehouses; _permissions = permissions;
    }

    public async Task<PagedResult<StockBalance>> BalancesAsync(ActorContext actor, PageRequest request,
        Guid? warehouseId, bool includeZero, CancellationToken ct = default)
    {
        await _permissions.RequireAsync(actor, ScreenCodes.StockReports, ActionCodes.View, ct: ct);
        return await _reports.ListBalancesAsync(request, warehouseId, includeZero, actor.IsAdmin,
            actor.AllowedBranchIds, ct);
    }

    public async Task<IReadOnlyList<Warehouse>> WarehouseOptionsAsync(ActorContext actor,
        CancellationToken ct = default)
    {
        await _permissions.RequireAsync(actor, ScreenCodes.StockReports, ActionCodes.View, ct: ct);
        return await _warehouses.ListOptionsAsync(PermissionScope.All, actor.UserId,
            actor.AllowedBranchIds, actor.IsAdmin, ct);
    }

    public async Task<StockCard> ItemCardAsync(ActorContext actor, Guid itemId, PageRequest request,
        Guid? warehouseId, DateTime? from, DateTime? to, CancellationToken ct = default)
    {
        await _permissions.RequireAsync(actor, ScreenCodes.StockReports, ActionCodes.View, ct: ct);
        var item = await _items.GetAsync(itemId, ct) ?? throw new NotFoundAppException();
        var movements = await _reports.ListMovementsAsync(request, itemId, warehouseId, from, to, ct);
        return new StockCard { Item = item, Movements = movements };
    }
}

public sealed class StockCard
{
    public Item Item { get; set; } = new();
    public PagedResult<StockMovement> Movements { get; set; } = new();
}
