using Microsoft.AspNetCore.Components;
using MudBlazor;
using Ubp.Application.Services;
using Ubp.Core.Models;
using Ubp.Core.Paging;
using Ubp.Core.Security;

namespace Ubp.Web.Pages.Accounting;

public partial class Periods
{
    [Inject] private PeriodService PeriodsService { get; set; } = default!;
    [Inject] private IDialogService DialogService { get; set; } = default!;

    private MudTable<FiscalPeriod> _table = default!;

    protected override string ScreenCode => ScreenCodes.FinancialPeriods;
    protected override Task OnLoadAsync() => Task.CompletedTask;

    private string Name(FiscalPeriod period) =>
        T.Language == "en" && !string.IsNullOrWhiteSpace(period.NameEn) ? period.NameEn! : period.NameAr;

    private async Task<TableData<FiscalPeriod>> LoadServerData(TableState state, CancellationToken ct)
    {
        var result = await PeriodsService.ListAsync(Actor!, new PageRequest
        {
            Page = state.Page + 1, PageSize = 25,
            SortBy = state.SortLabel ?? "start_date",
            Descending = state.SortLabel is null || state.SortDirection == SortDirection.Descending
        }, ct);
        return new TableData<FiscalPeriod> { Items = result.Items, TotalItems = result.Total };
    }

    private async Task ReloadAsync() => await _table.ReloadServerData();

    private async Task AddAsync()
    {
        var dialog = await DialogService.ShowAsync<PeriodDialog>(T.T("periods.add"), new DialogParameters());
        if ((await dialog.Result) is { Canceled: false }) { await ReloadAsync(); ShowSuccess("common.saved"); }
    }

    private async Task EditAsync(FiscalPeriod period)
    {
        var dialog = await DialogService.ShowAsync<PeriodDialog>(T.T("common.edit"),
            new DialogParameters { ["Model"] = period });
        if ((await dialog.Result) is { Canceled: false }) { await ReloadAsync(); ShowSuccess("common.saved"); }
    }

    private async Task DetailsAsync(FiscalPeriod period)
    {
        var dialog = await DialogService.ShowAsync<PeriodDetailDialog>(period.Code,
            new DialogParameters { ["Id"] = period.Id },
            new DialogOptions { MaxWidth = MaxWidth.ExtraLarge, FullWidth = true });
        if ((await dialog.Result) is { Canceled: false }) { await ReloadAsync(); }
    }

    private async Task DeleteAsync(FiscalPeriod period)
    {
        var confirmed = await DialogService.ShowMessageBoxAsync(T.T("common.confirm_delete"), period.Code,
            yesText: T.T("common.delete"), cancelText: T.T("common.cancel"));
        if (confirmed != true) return;
        await RunAsync(async () =>
        {
            await PeriodsService.DeleteAsync(Actor!, period.Id);
            await ReloadAsync();
            ShowSuccess("common.deleted");
        });
    }
}
