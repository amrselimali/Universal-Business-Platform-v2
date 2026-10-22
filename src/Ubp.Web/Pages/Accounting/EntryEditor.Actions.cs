using Microsoft.JSInterop;
using MudBlazor;
using Ubp.Core.Models;
using Ubp.Core.Results;
using Ubp.Core.Security;

namespace Ubp.Web.Pages.Accounting;

public partial class EntryEditor
{
    private async Task PickPartyAsync(JournalEntryLineInput line)
    {
        if (!IsDraft || !Can(ActionCodes.SelectParty)) return;
        var account = AccountOf(line);
        if (account is null || !account.RequiresParty) return;

        var kind = account.RequiresCustomer ? PartnerKind.Customer : PartnerKind.Supplier;
        var dialog = await DialogService.ShowAsync<PartyPickerDialog>(T.T("accounting.select_party"),
            new DialogParameters { ["Kind"] = kind });
        var result = await dialog.Result;
        if (result is not { Canceled: false } || result.Data is not PartyOption party) return;

        if (account.RequiresCustomer) line.CustomerId = party.Id;
        else line.SupplierId = party.Id;
        _partyNames[party.Id] = party.Name;
    }

    private async Task DistributeAsync(JournalEntryLineInput line) => await RunAsync(async () =>
    {
        var index = _input.Lines.IndexOf(line);
        if (index < 0) return;

        var dialog = await DialogService.ShowAsync<CostDistributionDialog>(T.T("accounting.distribute"),
            new DialogParameters { ["CostCenters"] = _costCenters, ["IsEnglish"] = IsEnglish });
        var result = await dialog.Result;
        if (result is not { Canceled: false } || result.Data is not List<CostDistributionLine> lines)
            return;

        await EntriesService.DistributeCostAsync(Actor!, _input.Lines, index, lines);
        ShowSuccess("accounting.distributed");
    });

    private async Task AutoBalanceAsync() => await RunAsync(async () =>
    {
        await EntriesService.AutoBalanceAsync(Actor!, _input.Lines, null);
        ShowSuccess("accounting.balanced");
    });

    private async Task SaveAsync() => await RunAsync(async () =>
    {
        _busy = true;
        try
        {
            _input.EntryDate = _entryDate ?? DateTime.Today;
            var detail = _detail is null
                ? await EntriesService.CreateAsync(Actor!, _input)
                : await EntriesService.UpdateAsync(Actor!, _detail.Header.Id, _detail.Header.Version, _input);

            _detail = detail;
            ShowSuccess("common.saved");
            Navigation.NavigateTo($"/accounting/entries/{detail.Header.Id}");
        }
        finally { _busy = false; }
    });

    private async Task PostAsync() => await RunAsync(async () =>
    {
        _busy = true;
        try
        {
            if (!IsBalanced)
                throw new ValidationAppException("validation.entry_not_balanced");

            _input.EntryDate = _entryDate ?? DateTime.Today;
            var detail = _detail is null
                ? await EntriesService.CreateAsync(Actor!, _input)
                : await EntriesService.UpdateAsync(Actor!, _detail.Header.Id, _detail.Header.Version, _input);

            await EntriesService.PostAsync(Actor!, detail.Header.Id);
            ShowSuccess("accounting.posted");
            Navigation.NavigateTo($"/accounting/entries/{detail.Header.Id}");
        }
        finally { _busy = false; }
    });

    private async Task ReverseAsync()
    {
        if (_detail is null) return;
        var confirmed = await DialogService.ShowMessageBoxAsync(T.T("accounting.reverse"),
            T.T("accounting.reverse_confirm"), yesText: T.T("accounting.reverse"),
            cancelText: T.T("common.cancel"));
        if (confirmed != true) return;

        await RunAsync(async () =>
        {
            var reversal = await EntriesService.ReverseAsync(Actor!, _detail.Header.Id,
                _detail.Header.EntryDate);
            ShowSuccess("accounting.reversed_done");
            Navigation.NavigateTo($"/accounting/entries/{reversal.Id}");
        });
    }

    private async Task PrintAsync()
    {
        try { await JS.InvokeVoidAsync("print"); }
        catch (Exception) { ShowError(new AppException("error.print")); }
    }
}
