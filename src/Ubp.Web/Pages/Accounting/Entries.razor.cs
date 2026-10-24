using Microsoft.AspNetCore.Components;
using MudBlazor;
using Ubp.Application.Services;
using Ubp.Core.Models;
using Ubp.Core.Paging;
using Ubp.Core.Security;

namespace Ubp.Web.Pages.Accounting;

public partial class Entries
{
    [Inject] private JournalEntryService EntriesService { get; set; } = default!;
    [Inject] private JournalService JournalsService { get; set; } = default!;
    [Inject] private IDialogService DialogService { get; set; } = default!;
    [Inject] private NavigationManager Navigation { get; set; } = default!;

    private MudTable<JournalEntry> _table = default!;
    private IReadOnlyList<Journal> _journals = Array.Empty<Journal>();
    private string? _search;
    private string? _status;
    private Guid? _journalId;
    private DateTime? _from = new DateTime(DateTime.Today.Year, 1, 1);
    private DateTime? _to = DateTime.Today;

    protected override string ScreenCode => ScreenCodes.JournalEntries;

    protected override async Task OnLoadAsync()
    {
        _journals = await JournalsService.OptionsAsync(Actor!);
    }

    private static Color StatusColor(string status) => status switch
    {
        EntryStatuses.Posted => Color.Success,
        EntryStatuses.Cancelled => Color.Default,
        _ => Color.Warning
    };

    private async Task<TableData<JournalEntry>> LoadServerData(TableState state, CancellationToken ct)
    {
        var result = await EntriesService.ListAsync(Actor!, new EntryFilter
        {
            Search = _search, Status = _status, JournalId = _journalId,
            From = _from, To = _to
        }, new PageRequest
        {
            Page = state.Page + 1, PageSize = 25, SortBy = state.SortLabel ?? "entry_date",
            Descending = state.SortDirection != SortDirection.Ascending
        }, ct);
        return new TableData<JournalEntry> { Items = result.Items, TotalItems = result.Total };
    }

    private async Task ReloadAsync() => await _table.ReloadServerData();

    private void NewEntry() => Navigation.NavigateTo("/accounting/entries/new");

    private void Open(JournalEntry entry) => Navigation.NavigateTo($"/accounting/entries/{entry.Id}");

    private async Task CopyAsync(JournalEntry entry) => await RunAsync(async () =>
    {
        var copy = await EntriesService.CopyAsync(Actor!, entry.Id);
        ShowSuccess("common.saved");
        Navigation.NavigateTo($"/accounting/entries/{copy.Header.Id}");
    });

    private async Task ReverseAsync(JournalEntry entry)
    {
        var confirmed = await DialogService.ShowMessageBoxAsync(T.T("accounting.reverse"),
            $"{entry.EntryNo} — {T.T("accounting.reverse_confirm")}",
            yesText: T.T("accounting.reverse"), cancelText: T.T("common.cancel"));
        if (confirmed != true) return;

        await RunAsync(async () =>
        {
            await EntriesService.ReverseAsync(Actor!, entry.Id, entry.EntryDate);
            await ReloadAsync();
            ShowSuccess("common.saved");
        });
    }

    private async Task CancelAsync(JournalEntry entry)
    {
        var confirmed = await DialogService.ShowMessageBoxAsync(T.T("common.cancel"),
            T.T("accounting.cancel_entry_confirm"), yesText: T.T("common.confirm"),
            cancelText: T.T("common.back"));
        if (confirmed != true) return;

        await RunAsync(async () =>
        {
            await EntriesService.CancelAsync(Actor!, entry.Id);
            await ReloadAsync();
            ShowSuccess("common.saved");
        });
    }
}
