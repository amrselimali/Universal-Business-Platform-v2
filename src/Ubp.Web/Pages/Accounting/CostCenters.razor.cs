using Microsoft.AspNetCore.Components;
using MudBlazor;
using Ubp.Application.Services;
using Ubp.Core.Models;
using Ubp.Core.Paging;
using Ubp.Core.Security;

namespace Ubp.Web.Pages.Accounting;

public partial class CostCenters
{
    [Inject] private CostCenterService CostCenterService { get; set; } = default!;
    [Inject] private IDialogService DialogService { get; set; } = default!;

    private MudTable<CostCenter> _table = default!;
    private string? _search;
    private bool _includeInactive;
    private bool IsEnglish => T.Language == "en";

    protected override string ScreenCode => ScreenCodes.CostCenters;
    protected override Task OnLoadAsync() => Task.CompletedTask;

    private async Task<TableData<CostCenter>> LoadServerData(TableState state, CancellationToken ct)
    {
        var result = await CostCenterService.ListAsync(Actor!, new CostCenterFilter
        {
            Search = _search, IncludeInactive = _includeInactive
        }, new PageRequest
        {
            Page = state.Page + 1, PageSize = 25, SortBy = state.SortLabel ?? "path",
            Descending = state.SortDirection == SortDirection.Descending
        }, ct);
        return new TableData<CostCenter> { Items = result.Items, TotalItems = result.Total };
    }

    private async Task ReloadAsync() => await _table.ReloadServerData();

    private async Task OnFilterChanged(bool value)
    {
        _includeInactive = value;
        await ReloadAsync();
    }

    private Task AddRootAsync() => OpenDialogAsync(null, null);

    private Task AddChildAsync(CostCenter parent) => OpenDialogAsync(null, parent);

    private Task EditAsync(CostCenter costCenter) => OpenDialogAsync(costCenter, null);

    private async Task OpenDialogAsync(CostCenter? model, CostCenter? parent)
    {
        var dialog = await DialogService.ShowAsync<CostCenterDialog>(T.T("screen.cost_centers"),
            new DialogParameters
            {
                ["Model"] = model, ["ParentId"] = parent?.Id, ["ParentName"] = parent?.NameAr
            });
        if ((await dialog.Result) is { Canceled: false })
        {
            await ReloadAsync();
            ShowSuccess("common.saved");
        }
    }

    private async Task ToggleAsync(CostCenter costCenter, bool value) => await RunAsync(async () =>
    {
        await CostCenterService.SetActiveAsync(Actor!, costCenter.Id, value);
        await ReloadAsync();
        ShowSuccess("common.saved");
    });

    private async Task DeleteAsync(CostCenter costCenter)
    {
        var confirmed = await DialogService.ShowMessageBoxAsync(T.T("common.confirm_delete"),
            $"{costCenter.Code} — {costCenter.NameAr}", yesText: T.T("common.delete"),
            cancelText: T.T("common.cancel"));
        if (confirmed != true) return;

        await RunAsync(async () =>
        {
            await CostCenterService.DeleteAsync(Actor!, costCenter.Id);
            await ReloadAsync();
            ShowSuccess("common.deleted");
        });
    }

    private async Task MoveAsync(CostCenter costCenter) => await RunAsync(async () =>
    {
        var all = await CostCenterService.OptionsAsync(Actor!, null);
        var options = all.Where(c => c.Id != costCenter.Id)
            .Select(c => new TreeMoveOption(c.Id, $"{c.Code} — {c.DisplayName(IsEnglish)}", c.Level))
            .ToList();

        var dialog = await DialogService.ShowAsync<TreeMoveDialog>(T.T("accounting.move"),
            new DialogParameters
            {
                ["Options"] = options, ["Title"] = T.T("accounting.move"), ["AllowRoot"] = true
            });
        var result = await dialog.Result;
        if (result is { Canceled: true }) return;

        await CostCenterService.MoveAsync(Actor!, costCenter.Id,
            result?.Data is Guid parent && parent != Guid.Empty ? parent : null);
        await ReloadAsync();
        ShowSuccess("common.saved");
    });
}
