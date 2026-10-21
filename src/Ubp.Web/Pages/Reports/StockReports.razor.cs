using Microsoft.AspNetCore.Components;
using MudBlazor;
using Ubp.Application.Services;
using Ubp.Core.Models;
using Ubp.Core.Paging;
using Ubp.Core.Security;

namespace Ubp.Web.Pages.Reports;

public partial class StockReports
{
    [Inject] private StockReportService Reports { get; set; } = default!;
    [Inject] private ItemService Items { get; set; } = default!;
    [Inject] private WarehouseService Warehouses { get; set; } = default!;

    private MudTable<StockBalance> _balancesTable = default!;
    private Guid? _balanceWarehouse;
    private bool _includeZero;
    private IReadOnlyList<Warehouse> _warehouses = Array.Empty<Warehouse>();

    private ItemOption? _item;
    private Guid? _cardWarehouse;
    private DateTime? _from;
    private DateTime? _to;
    private StockCard? _card;

    protected override string ScreenCode => ScreenCodes.StockReports;

    protected override async Task OnLoadAsync()
    {
        _warehouses = await Warehouses.OptionsAsync(Actor!);
    }

    private async Task<TableData<StockBalance>> LoadBalances(TableState state, CancellationToken ct)
    {
        var result = await Reports.BalancesAsync(Actor!, new PageRequest
        {
            Page = state.Page + 1, PageSize = 25,
            SortBy = state.SortLabel ?? "code",
            Descending = state.SortDirection == SortDirection.Descending
        }, _balanceWarehouse, _includeZero, ct);
        return new TableData<StockBalance> { Items = result.Items, TotalItems = result.Total };
    }

    private async Task ReloadBalancesAsync() => await _balancesTable.ReloadServerData();
    private async Task OnZeroChanged(bool value) { _includeZero = value; await ReloadBalancesAsync(); }

    private async Task<IEnumerable<ItemOption>> SearchItems(string? search, CancellationToken ct) =>
        await Items.OptionsAsync(Actor!, search);

    private string ItemLabel(ItemOption? item) => item is null ? "" : $"{item.Code} — {item.NameAr}";

    private async Task LoadCardAsync()
    {
        if (_item is null) return;
        await RunAsync(async () =>
        {
            _card = await Reports.ItemCardAsync(Actor!, _item.Id, new PageRequest { PageSize = 25 },
                _cardWarehouse, _from, _to);
        });
    }
}
