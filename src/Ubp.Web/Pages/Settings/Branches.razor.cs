using Microsoft.AspNetCore.Components;
using MudBlazor;
using Ubp.Application.Services;
using Ubp.Core.Models;
using Ubp.Core.Paging;
using Ubp.Core.Security;

namespace Ubp.Web.Pages.Settings;

public partial class Branches
{
    [Inject] private BranchService BranchService { get; set; } = default!;
    [Inject] private IDialogService DialogService { get; set; } = default!;

    private MudTable<Branch> _table = default!;
    private string? _search;
    private bool _includeInactive;

    protected override string ScreenCode => ScreenCodes.Branches;

    protected override Task OnLoadAsync() => Task.CompletedTask;

    private async Task<TableData<Branch>> LoadServerData(TableState state, CancellationToken ct)
    {
        var request = new PageRequest
        {
            Page = state.Page + 1,
            PageSize = 25,
            Search = _search,
            IncludeInactive = _includeInactive,
            SortBy = state.SortLabel ?? "code",
            Descending = state.SortDirection == SortDirection.Descending
        };
        var result = await BranchService.ListAsync(Actor!, request, ct);
        return new TableData<Branch> { Items = result.Items, TotalItems = result.Total };
    }

    private async Task ReloadAsync() => await _table.ReloadServerData();
    private async Task OnFilterChanged(bool value) { _includeInactive = value; await ReloadAsync(); }

    private async Task AddAsync()
    {
        var dialog = await DialogService.ShowAsync<BranchDialog>(T.T("branches.add"), new DialogParameters());
        var result = await dialog.Result;
        if (result is { Canceled: false })
        {
            await _table.ReloadServerData();
            ShowSuccess("branch.saved");
        }
    }

    private async Task EditAsync(Branch branch)
    {
        var dialog = await DialogService.ShowAsync<BranchDialog>(T.T("branches.edit"),
            new DialogParameters { ["Model"] = branch });
        var result = await dialog.Result;
        if (result is { Canceled: false })
        {
            await _table.ReloadServerData();
            ShowSuccess("branch.saved");
        }
    }

    private async Task ToggleAsync(Branch branch, bool value) =>
        await RunAsync(async () =>
        {
            await BranchService.SetActiveAsync(Actor!, branch.Id, value);
            await _table.ReloadServerData();
            ShowSuccess("common.saved");
        });

    private async Task DeleteAsync(Branch branch)
    {
        var confirmed = await DialogService.ShowMessageBoxAsync(T.T("common.confirm_delete"),
            T.T("branches.delete_confirm", branch.NameAr), yesText: T.T("common.delete"),
            cancelText: T.T("common.cancel"));
        if (confirmed != true) return;
        await RunAsync(async () =>
        {
            var outcome = await BranchService.DeleteAsync(Actor!, branch.Id);
            await _table.ReloadServerData();
            ShowSuccess(outcome.MessageKey);
        });
    }
}
