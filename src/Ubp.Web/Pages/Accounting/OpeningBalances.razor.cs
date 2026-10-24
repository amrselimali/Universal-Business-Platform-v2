using Microsoft.AspNetCore.Components;
using MudBlazor;
using Ubp.Application.Services;
using Ubp.Core.Models;
using Ubp.Core.Results;
using Ubp.Core.Security;

namespace Ubp.Web.Pages.Accounting;

public partial class OpeningBalances
{
    [Inject] private OpeningBalanceService OpeningService { get; set; } = default!;
    [Inject] private AccountService AccountsService { get; set; } = default!;
    [Inject] private CostCenterService CostCenterServiceInject { get; set; } = default!;
    [Inject] private BranchService BranchService { get; set; } = default!;
    [Inject] private IDialogService DialogService { get; set; } = default!;

    private List<JournalEntryLineInput> _lines = new();
    private JournalEntryDetail? _detail;
    private IReadOnlyList<Account> _accounts = Array.Empty<Account>();
    private IReadOnlyList<CostCenter> _costCenters = Array.Empty<CostCenter>();
    private IReadOnlyList<Branch> _branches = Array.Empty<Branch>();
    private readonly Dictionary<Guid, string> _partyNames = new();
    private Guid _branchId;
    private DateTime? _entryDate = DateTime.Today;
    private string? _description;
    private bool _busy;
    private bool IsEnglish => T.Language == "en";

    protected override string ScreenCode => ScreenCodes.OpeningBalances;
    private bool IsDraft => _detail is null || _detail.Header.IsDraft;
    private decimal Difference => _lines.Sum(l => l.Debit) - _lines.Sum(l => l.Credit);

    protected override async Task OnLoadAsync()
    {
        _accounts = await AccountsService.PostableAsync(Actor!);
        _branches = await BranchService.OptionsAsync(Actor!);
        _branchId = Actor!.CurrentBranchId ?? _branches.FirstOrDefault()?.Id ?? Guid.Empty;
        _costCenters = await CostCenterServiceInject.OptionsAsync(Actor!, _branchId);
        await LoadDraftAsync();
    }

    private async Task LoadDraftAsync()
    {
        _detail = await OpeningService.GetDraftAsync(Actor!, _branchId);
        _lines = new List<JournalEntryLineInput>();
        _partyNames.Clear();

        if (_detail is null)
        {
            _entryDate = DateTime.Today;
            _lines.Add(new JournalEntryLineInput());
            return;
        }

        _entryDate = _detail.Header.EntryDate;
        _description = _detail.Header.Description;
        foreach (var line in _detail.Lines)
        {
            _lines.Add(new JournalEntryLineInput
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
    }

    private async Task OnBranchChanged(Guid branchId)
    {
        _branchId = branchId;
        _costCenters = await CostCenterServiceInject.OptionsAsync(Actor!, branchId);
        await RunAsync(LoadDraftAsync);
    }

    private void AddLine() => _lines.Add(new JournalEntryLineInput());

    private void RemoveLine(JournalEntryLineInput line)
    {
        _lines.Remove(line);
        if (_lines.Count == 0) AddLine();
    }

    private void OnAmount(JournalEntryLineInput line, decimal value, bool debit)
    {
        if (debit) { line.Debit = Math.Max(0, value); if (line.Debit > 0) line.Credit = 0; }
        else { line.Credit = Math.Max(0, value); if (line.Credit > 0) line.Debit = 0; }
    }

    private void OnAccountChanged(JournalEntryLineInput line, Guid accountId)
    {
        line.AccountId = accountId;
        var account = _accounts.FirstOrDefault(a => a.Id == accountId);
        if (account is null) return;
        if (!account.RequiresCustomer) line.CustomerId = null;
        if (!account.RequiresSupplier) line.SupplierId = null;
        if (account.CostCenterPolicy == CostCenterPolicies.NotAllowed) line.CostCenterId = null;
    }

    private Account? AccountOf(JournalEntryLineInput line) =>
        _accounts.FirstOrDefault(a => a.Id == line.AccountId);

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
