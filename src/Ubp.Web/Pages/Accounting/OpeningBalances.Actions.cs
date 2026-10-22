using MudBlazor;
using Ubp.Core.Models;
using Ubp.Core.Results;
using Ubp.Core.Security;

namespace Ubp.Web.Pages.Accounting;

public partial class OpeningBalances
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

    private async Task ImportAsync() => await RunAsync(async () =>
    {
        var dialog = await DialogService.ShowAsync<OpeningCsvDialog>(T.T("accounting.import_csv"),
            new DialogParameters());
        var result = await dialog.Result;
        if (result is not { Canceled: false } || result.Data is not OpeningBalanceImportResult import)
            return;

        _lines.RemoveAll(l => l.AccountId == Guid.Empty && l.Debit == 0 && l.Credit == 0);
        _lines.AddRange(import.Lines);
        Snackbar.Add($"{T.T("accounting.imported")}: {import.Lines.Count}", Severity.Success);
        if (import.Errors.Count > 0)
            Snackbar.Add($"{T.T("accounting.import_errors")}: {import.Errors.Count}", Severity.Warning);
    });

    private async Task AutoBalanceAsync() => await RunAsync(async () =>
    {
        await OpeningService.AutoBalanceEquityAsync(Actor!, _lines, null);
        ShowSuccess("accounting.balanced");
    });

    private async Task SaveAsync() => await RunAsync(async () =>
    {
        _busy = true;
        try
        {
            _detail = await OpeningService.SaveDraftAsync(Actor!, new OpeningBalanceInput
            {
                BranchId = _branchId,
                EntryDate = _entryDate ?? DateTime.Today,
                Description = _description,
                Lines = _lines.Where(l => l.AccountId != Guid.Empty).ToList()
            });
            ShowSuccess("common.saved");
        }
        finally { _busy = false; }
    });

    private async Task PostAsync() => await RunAsync(async () =>
    {
        _busy = true;
        try
        {
            if (Difference != 0)
                throw new ValidationAppException("validation.entry_not_balanced");

            var detail = await OpeningService.SaveDraftAsync(Actor!, new OpeningBalanceInput
            {
                BranchId = _branchId,
                EntryDate = _entryDate ?? DateTime.Today,
                Description = _description,
                Lines = _lines.Where(l => l.AccountId != Guid.Empty).ToList()
            });
            await OpeningService.PostAsync(Actor!, detail.Header.Id);
            _detail = detail;
            ShowSuccess("accounting.posted");
        }
        finally { _busy = false; }
    });

    private void OpenEntry()
    {
        if (_detail is null) return;
        Navigation.NavigateTo($"/accounting/entries/{_detail.Header.Id}");
    }
}
