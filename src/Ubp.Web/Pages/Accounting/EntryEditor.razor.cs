using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using MudBlazor;
using Ubp.Application.Services;
using Ubp.Core.Models;
using Ubp.Core.Results;
using Ubp.Core.Security;

namespace Ubp.Web.Pages.Accounting;

public partial class EntryEditor
{
    [Parameter] public Guid? Id { get; set; }

    [Inject] private JournalEntryService EntriesService { get; set; } = default!;
    [Inject] private JournalService JournalsService { get; set; } = default!;
    [Inject] private AccountService AccountsService { get; set; } = default!;
    [Inject] private CostCenterService CostCenterServiceInject { get; set; } = default!;
    [Inject] private BranchService BranchService { get; set; } = default!;
    [Inject] private IDialogService DialogService { get; set; } = default!;
    [Inject] private IJSRuntime JS { get; set; } = default!;

    private readonly JournalEntryInput _input = new();
    private JournalEntryDetail? _detail;
    private IReadOnlyList<Account> _accounts = Array.Empty<Account>();
    private IReadOnlyList<CostCenter> _costCenters = Array.Empty<CostCenter>();
    private IReadOnlyList<Branch> _branches = Array.Empty<Branch>();
    private IReadOnlyList<Journal> _journals = Array.Empty<Journal>();
    private readonly Dictionary<Guid, string> _partyNames = new();
    private DateTime? _entryDate = DateTime.Today;
    private bool _busy;
    private bool IsEnglish => T.Language == "en";

    protected override string ScreenCode => ScreenCodes.JournalEntry;
    private bool IsDraft => _detail is null || _detail.Header.IsDraft;
    private decimal Difference => _input.Lines.Sum(l => l.Debit) - _input.Lines.Sum(l => l.Credit);
    private bool IsBalanced => Difference == 0 && _input.Lines.Any(l => l.Debit > 0);

    protected override async Task OnLoadAsync()
    {
        _accounts = await AccountsService.PostableAsync(Actor!);
        _costCenters = await CostCenterServiceInject.OptionsAsync(Actor!, Actor!.CurrentBranchId);
        _branches = await BranchService.OptionsAsync(Actor!);
        _journals = await JournalsService.OptionsAsync(Actor!);

        _input.BranchId = Actor!.CurrentBranchId ?? _branches.FirstOrDefault()?.Id ?? Guid.Empty;
        _input.JournalId = (_journals.FirstOrDefault(j => j.AllowManual) ?? _journals.FirstOrDefault())?.Id
                           ?? Guid.Empty;
        _input.EntryDate = DateTime.Today;

        if (Id is Guid id)
        {
            _detail = await EntriesService.GetAsync(Actor!, id);
            var header = _detail.Header;
            _input.JournalId = header.JournalId;
            _input.BranchId = header.BranchId;
            _input.EntryDate = header.EntryDate;
            _input.Reference = header.Reference;
            _input.Description = header.Description;
            _entryDate = header.EntryDate;
            _input.Lines.Clear();
            foreach (var line in _detail.Lines)
            {
                _input.Lines.Add(new JournalEntryLineInput
                {
                    AccountId = line.AccountId, Description = line.Description, Debit = line.Debit,
                    Credit = line.Credit, CustomerId = line.CustomerId, SupplierId = line.SupplierId,
                    CostCenterId = line.CostCenterId
                });
                if (line.CustomerId is not null && line.CustomerName is not null)
                    _partyNames[line.CustomerId.Value] = line.CustomerName;
                if (line.SupplierId is not null && line.SupplierName is not null)
                    _partyNames[line.SupplierId.Value] = line.SupplierName;
            }
            return;
        }

        AddLine();
        AddLine();
    }

    private string Subtitle() => _detail is null
        ? T.T("accounting.new_entry")
        : $"{_detail.Header.EntryNo ?? T.T("accounting.no_number")} — {T.T("accounting.status_" + _detail.Header.Status)}";

    private Account? AccountOf(JournalEntryLineInput line) =>
        _accounts.FirstOrDefault(a => a.Id == line.AccountId);

    private void AddLine() => _input.Lines.Add(new JournalEntryLineInput());

    private void RemoveLine(JournalEntryLineInput line)
    {
        _input.Lines.Remove(line);
        if (_input.Lines.Count == 0) AddLine();
    }

    private void OnAmount(JournalEntryLineInput line, decimal value, bool debit)
    {
        if (debit)
        {
            line.Debit = value < 0 ? 0 : value;
            if (line.Debit > 0) line.Credit = 0;
        }
        else
        {
            line.Credit = value < 0 ? 0 : value;
            if (line.Credit > 0) line.Debit = 0;
        }
    }

    private void OnAccountChanged(JournalEntryLineInput line, Guid accountId)
    {
        _ = ChangeAccountAsync(line, accountId);
    }

    private async Task ChangeAccountAsync(JournalEntryLineInput line, Guid accountId)
    {
        line.AccountId = accountId;
        var account = AccountOf(line);
        if (account is null) return;

        if (!account.RequiresCustomer) line.CustomerId = null;
        if (!account.RequiresSupplier) line.SupplierId = null;
        if (account.CostCenterPolicy == CostCenterPolicies.NotAllowed) line.CostCenterId = null;

        // الحساب الذي يتطلب طرفاً يفتح صندوق الاختيار تلقائياً (والإلغاء يتركه فارغاً).
        if (account.RequiresParty && line.CustomerId is null && line.SupplierId is null)
            await PickPartyAsync(line);
    }

    private string PartyLabel(JournalEntryLineInput line)
    {
        var id = line.CustomerId ?? line.SupplierId;
        if (id is null) return T.T("accounting.select_party");
        return _partyNames.TryGetValue(id.Value, out var name) ? name : "…";
    }

    private Color PartyColor(JournalEntryLineInput line)
    {
        var account = AccountOf(line);
        if (account is null || !account.RequiresParty) return Color.Default;
        return line.CustomerId is null && line.SupplierId is null ? Color.Error : Color.Success;
    }
}
