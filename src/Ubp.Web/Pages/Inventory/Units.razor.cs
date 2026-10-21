using Microsoft.AspNetCore.Components;
using MudBlazor;
using Ubp.Application.Services;
using Ubp.Core.Models;
using Ubp.Core.Paging;
using Ubp.Core.Security;

namespace Ubp.Web.Pages.Inventory;

public partial class Units
{
    [Inject] private UnitService UnitService { get; set; } = default!;
    [Inject] private IDialogService DialogService { get; set; } = default!;

    private MudTable<Unit> _table = default!;
    private string? _search;
    private bool _includeInactive;

    protected override string ScreenCode => ScreenCodes.Units;

    protected override Task OnLoadAsync() => Task.CompletedTask;

    private string Name(Unit unit) => T.Language == "en" && !string.IsNullOrWhiteSpace(unit.NameEn) ? unit.NameEn! : unit.NameAr;

    private async Task<TableData<Unit>> LoadServerData(TableState state, CancellationToken ct)
    {
        var result = await UnitService.ListAsync(Actor!, new PageRequest
        {
            Page = state.Page + 1, PageSize = 25, Search = _search, IncludeInactive = _includeInactive,
            SortBy = state.SortLabel ?? "code", Descending = state.SortDirection == SortDirection.Descending
        }, ct);
        return new TableData<Unit> { Items = result.Items, TotalItems = result.Total };
    }

    private async Task ReloadAsync() => await _table.ReloadServerData();
    private async Task OnFilterChanged(bool value) { _includeInactive = value; await ReloadAsync(); }

    private async Task AddAsync()
    {
        var dialog = await DialogService.ShowAsync<UnitDialog>(T.T("common.add"), new DialogParameters());
        if ((await dialog.Result) is { Canceled: false }) { await ReloadAsync(); ShowSuccess("common.saved"); }
    }

    private async Task EditAsync(Unit unit)
    {
        var dialog = await DialogService.ShowAsync<UnitDialog>(T.T("common.edit"),
            new DialogParameters { ["Model"] = unit });
        if ((await dialog.Result) is { Canceled: false }) { await ReloadAsync(); ShowSuccess("common.saved"); }
    }

    private async Task ToggleAsync(Unit unit, bool value) => await RunAsync(async () =>
    {
        await UnitService.SetActiveAsync(Actor!, unit.Id, value);
        await ReloadAsync();
        ShowSuccess("common.saved");
    });

    private async Task DeleteAsync(Unit unit)
    {
        var confirmed = await DialogService.ShowMessageBoxAsync(T.T("common.confirm_delete"),
            T.T("common.confirm_delete") + " " + unit.NameAr, yesText: T.T("common.delete"),
            cancelText: T.T("common.cancel"));
        if (confirmed != true) return;
        await RunAsync(async () =>
        {
            await UnitService.DeleteAsync(Actor!, unit.Id);
            await ReloadAsync();
            ShowSuccess("common.deleted");
        });
    }
}
