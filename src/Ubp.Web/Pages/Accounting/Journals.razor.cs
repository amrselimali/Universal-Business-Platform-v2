using Microsoft.AspNetCore.Components;
using MudBlazor;
using Ubp.Application.Services;
using Ubp.Core.Models;
using Ubp.Core.Paging;
using Ubp.Core.Security;

namespace Ubp.Web.Pages.Accounting;

public partial class Journals
{
    [Inject] private JournalService JournalService { get; set; } = default!;
    [Inject] private IDialogService DialogService { get; set; } = default!;

    private MudTable<Journal> _table = default!;
    private string? _search;
    private bool _includeInactive;
    private bool IsEnglish => T.Language == "en";

    protected override string ScreenCode => ScreenCodes.Journals;
    protected override Task OnLoadAsync() => Task.CompletedTask;

    private async Task<TableData<Journal>> LoadServerData(TableState state, CancellationToken ct)
    {
        var result = await JournalService.ListAsync(Actor!, new JournalFilter
        {
            Search = _search, IncludeInactive = _includeInactive
        }, new PageRequest
        {
            Page = state.Page + 1, PageSize = 25, SortBy = state.SortLabel ?? "sort_order",
            Descending = state.SortDirection == SortDirection.Descending
        }, ct);
        return new TableData<Journal> { Items = result.Items, TotalItems = result.Total };
    }

    private async Task ReloadAsync() => await _table.ReloadServerData();

    private async Task OnFilterChanged(bool value)
    {
        _includeInactive = value;
        await ReloadAsync();
    }

    private async Task AddAsync() => await OpenDialogAsync(null);

    private async Task EditAsync(Journal journal) => await OpenDialogAsync(journal);

    private async Task OpenDialogAsync(Journal? model)
    {
        var dialog = await DialogService.ShowAsync<JournalDialog>(T.T("screen.journals"),
            new DialogParameters { ["Model"] = model });
        if ((await dialog.Result) is { Canceled: false })
        {
            await ReloadAsync();
            ShowSuccess("common.saved");
        }
    }

    private async Task ToggleAsync(Journal journal, bool value) => await RunAsync(async () =>
    {
        await JournalService.SetActiveAsync(Actor!, journal.Id, value);
        await ReloadAsync();
        ShowSuccess("common.saved");
    });

    private async Task DeleteAsync(Journal journal)
    {
        var confirmed = await DialogService.ShowMessageBoxAsync(T.T("common.confirm_delete"),
            $"{journal.Code} — {journal.NameAr}", yesText: T.T("common.delete"),
            cancelText: T.T("common.cancel"));
        if (confirmed != true) return;

        await RunAsync(async () =>
        {
            await JournalService.DeleteAsync(Actor!, journal.Id);
            await ReloadAsync();
            ShowSuccess("common.deleted");
        });
    }
}
