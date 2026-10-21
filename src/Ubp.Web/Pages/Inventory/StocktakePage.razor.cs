using Microsoft.AspNetCore.Components;
using MudBlazor;
using Ubp.Application.Services;
using Ubp.Core.Models;
using Ubp.Core.Paging;
using Ubp.Core.Security;

namespace Ubp.Web.Pages.Inventory;

public partial class StocktakePage
{
    [Inject] private StocktakeService Stocktakes { get; set; } = default!;
    [Inject] private IDialogService DialogService { get; set; } = default!;

    private MudTable<Stocktake> _table = default!;
    private string? _search;
    private string? _status;

    protected override string ScreenCode => ScreenCodes.Stocktake;
    protected override Task OnLoadAsync() => Task.CompletedTask;

    private async Task<TableData<Stocktake>> LoadServerData(TableState state, CancellationToken ct)
    {
        var result = await Stocktakes.ListAsync(Actor!, new PageRequest
        {
            Page = state.Page + 1, PageSize = 25, Search = _search,
            SortBy = state.SortLabel ?? "created_at",
            Descending = state.SortLabel is null || state.SortDirection == SortDirection.Descending
        }, _status, ct);
        return new TableData<Stocktake> { Items = result.Items, TotalItems = result.Total };
    }

    private async Task ReloadAsync() => await _table.ReloadServerData();

    private async Task AddAsync()
    {
        var dialog = await DialogService.ShowAsync<StocktakeDialog>(T.T("stocktake.add"), new DialogParameters());
        if ((await dialog.Result) is { Canceled: false }) { await ReloadAsync(); ShowSuccess("common.saved"); }
    }

    private async Task CountAsync(Stocktake stocktake)
    {
        var dialog = await DialogService.ShowAsync<StocktakeCountDialog>(stocktake.Code,
            new DialogParameters { ["Id"] = stocktake.Id },
            new DialogOptions { MaxWidth = MaxWidth.ExtraLarge, FullWidth = true });
        if ((await dialog.Result) is { Canceled: false }) { await ReloadAsync(); ShowSuccess("common.saved"); }
    }

    private async Task PostAsync(Stocktake stocktake)
    {
        var confirmed = await DialogService.ShowMessageBoxAsync(T.T("stocktake.post"),
            T.T("stocktake.post_confirm"), yesText: T.T("stocktake.post"), cancelText: T.T("common.cancel"));
        if (confirmed != true) return;
        await RunAsync(async () =>
        {
            await Stocktakes.PostAsync(Actor!, stocktake.Id);
            await ReloadAsync();
            ShowSuccess("stocktake.posted");
        });
    }

    private async Task DeleteAsync(Stocktake stocktake)
    {
        var confirmed = await DialogService.ShowMessageBoxAsync(T.T("common.confirm_delete"), stocktake.Code,
            yesText: T.T("common.delete"), cancelText: T.T("common.cancel"));
        if (confirmed != true) return;
        await RunAsync(async () =>
        {
            await Stocktakes.DeleteAsync(Actor!, stocktake.Id);
            await ReloadAsync();
            ShowSuccess("common.deleted");
        });
    }
}
