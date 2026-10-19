using Microsoft.AspNetCore.Components;
using MudBlazor;
using Ubp.Application.Services;
using Ubp.Core.Models;
using Ubp.Core.Paging;
using Ubp.Core.Security;

namespace Ubp.Web.Pages.Settings;

public partial class Warehouses
{
    [Inject] private WarehouseService WarehousesService { get; set; } = default!;
    [Inject] private BranchService BranchService { get; set; } = default!;
    [Inject] private IDialogService DialogService { get; set; } = default!;

    private MudTable<Warehouse> _table = default!;
    private string? _search;
    private bool _includeInactive;
    private Dictionary<Guid, string> _branchNames = new();

    protected override string ScreenCode => ScreenCodes.Warehouses;

    protected override async Task OnLoadAsync()
    {
        await LoadBranchesAsync();
    }

    private async Task LoadBranchesAsync()
    {
        var branches = await BranchService.OptionsAsync(Actor!);
        _branchNames = branches.ToDictionary(b => b.Id, b => b.NameAr);
    }

    private string BranchNames(Warehouse warehouse) =>
        warehouse.BranchIds.Count == 0
            ? "—"
            : string.Join("، ", warehouse.BranchIds.Select(id => _branchNames.GetValueOrDefault(id, "—")));

    private async Task<TableData<Warehouse>> LoadServerData(TableState state, CancellationToken ct)
    {
        var request = new PageRequest
        {
            Page = state.Page + 1, PageSize = 25, Search = _search,
            IncludeInactive = _includeInactive, SortBy = state.SortLabel ?? "code",
            Descending = state.SortDirection == SortDirection.Descending
        };
        var result = await WarehousesService.ListAsync(Actor!, request, ct);
        return new TableData<Warehouse> { Items = result.Items, TotalItems = result.Total };
    }

    private async Task ReloadAsync() => await _table.ReloadServerData();
    private async Task OnFilterChanged(bool value) { _includeInactive = value; await ReloadAsync(); }

    private async Task AddAsync()
    {
        var dialog = await DialogService.ShowAsync<WarehouseDialog>(T.T("warehouses.add"), new DialogParameters());
        if ((await dialog.Result) is { Canceled: false })
        {
            await _table.ReloadServerData();
            ShowSuccess("warehouse.saved");
        }
    }

    private async Task EditAsync(Warehouse warehouse)
    {
        var dialog = await DialogService.ShowAsync<WarehouseDialog>(T.T("warehouses.edit"),
            new DialogParameters { ["Model"] = warehouse });
        if ((await dialog.Result) is { Canceled: false })
        {
            await LoadBranchesAsync();
            await _table.ReloadServerData();
            ShowSuccess("warehouse.saved");
        }
    }

    private async Task ToggleAsync(Warehouse warehouse, bool value) =>
        await RunAsync(async () =>
        {
            await WarehousesService.SetActiveAsync(Actor!, warehouse.Id, value);
            await _table.ReloadServerData();
            ShowSuccess("common.saved");
        });

    private async Task DeleteAsync(Warehouse warehouse)
    {
        var confirmed = await DialogService.ShowMessageBoxAsync(T.T("common.confirm_delete"),
            T.T("warehouses.delete_confirm", warehouse.NameAr), yesText: T.T("common.delete"),
            cancelText: T.T("common.cancel"));
        if (confirmed != true) return;
        await RunAsync(async () =>
        {
            var outcome = await WarehousesService.DeleteAsync(Actor!, warehouse.Id);
            await _table.ReloadServerData();
            ShowSuccess(outcome.MessageKey);
        });
    }
}
