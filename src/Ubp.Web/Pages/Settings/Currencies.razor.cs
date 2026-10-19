using Microsoft.AspNetCore.Components;
using MudBlazor;
using Ubp.Application.Services;
using Ubp.Core.Models;
using Ubp.Core.Paging;
using Ubp.Core.Security;

namespace Ubp.Web.Pages.Settings;

public partial class Currencies
{
    [Inject] private CurrencyService CurrenciesService { get; set; } = default!;
    [Inject] private IDialogService DialogService { get; set; } = default!;

    private MudTable<Currency> _table = default!;
    private string? _search;

    protected override string ScreenCode => ScreenCodes.Currencies;

    protected override Task OnLoadAsync() => Task.CompletedTask;

    private async Task<TableData<Currency>> LoadServerData(TableState state, CancellationToken ct)
    {
        var request = new PageRequest
        {
            Page = state.Page + 1, PageSize = 25, Search = _search,
            SortBy = state.SortLabel ?? "is_base",
            Descending = state.SortDirection != SortDirection.Ascending
        };
        var result = await CurrenciesService.ListAsync(Actor!, request, ct);
        return new TableData<Currency> { Items = result.Items, TotalItems = result.Total };
    }

    private async Task ReloadAsync() => await _table.ReloadServerData();

    private async Task AddAsync()
    {
        var dialog = await DialogService.ShowAsync<CurrencyDialog>(T.T("currencies.add"), new DialogParameters());
        if ((await dialog.Result) is { Canceled: false })
        {
            await _table.ReloadServerData();
            ShowSuccess("currency.saved");
        }
    }

    private async Task EditAsync(Currency currency)
    {
        var dialog = await DialogService.ShowAsync<CurrencyDialog>(T.T("currencies.edit"),
            new DialogParameters { ["Model"] = currency });
        if ((await dialog.Result) is { Canceled: false })
        {
            await _table.ReloadServerData();
            ShowSuccess("currency.saved");
        }
    }

    private async Task DeleteAsync(Currency currency)
    {
        var confirmed = await DialogService.ShowMessageBoxAsync(T.T("common.confirm_delete"),
            T.T("currencies.delete_confirm", currency.NameAr), yesText: T.T("common.delete"),
            cancelText: T.T("common.cancel"));
        if (confirmed != true) return;
        await RunAsync(async () =>
        {
            await CurrenciesService.DeleteAsync(Actor!, currency.Id);
            await _table.ReloadServerData();
            ShowSuccess("currency.deleted");
        });
    }
}
