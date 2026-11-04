using Microsoft.AspNetCore.Components;
using MudBlazor;
using Ubp.Application.Services;
using Ubp.Core.Models;
using TreasuryModel = Ubp.Core.Models.Treasury;
using Ubp.Core.Security;
using Ubp.Web.Services;

namespace Ubp.Web.Pages.Expenses;

public partial class OperatingExpenseDialog : ComponentBase
{
    [CascadingParameter] private IMudDialogInstance MudDialog { get; set; } = default!;
    [Parameter] public Guid? BranchId { get; set; }

    [Inject] private OperatingExpenseService Expenses { get; set; } = default!;
    [Inject] private LookupService Lookup { get; set; } = default!;
    [Inject] private AccountService Accounts { get; set; } = default!;
    [Inject] private BranchService BranchApi { get; set; } = default!;
    [Inject] private TreasuryService TreasuryApi { get; set; } = default!;
    [Inject] private CostCenterService CostCenters { get; set; } = default!;
    [Inject] private UserContext UserContext { get; set; } = default!;
    [Inject] private UiCulture T { get; set; } = default!;
    [Inject] private ISnackbar Snackbar { get; set; } = default!;

    private sealed class LineModel
    {
        public string? Description { get; set; }
        public Account? Account { get; set; }
        public Guid? CostCenterId { get; set; }
        public ItemOption? Item { get; set; }
        public Guid? WarehouseId { get; set; }
        public decimal Quantity { get; set; } = 1;
        public decimal UnitPrice { get; set; }
        public decimal Amount => Math.Round(Quantity * UnitPrice, 2, MidpointRounding.AwayFromZero);
    }

    private MudForm _form = default!;
    private readonly List<LineModel> _lines = new();
    private IReadOnlyList<Branch> Branches = Array.Empty<Branch>();
    private IReadOnlyList<Warehouse> _warehouses = Array.Empty<Warehouse>();
    private IReadOnlyList<Account> _accounts = Array.Empty<Account>();
    private IReadOnlyList<CostCenter> _costCenters = Array.Empty<CostCenter>();
    private IReadOnlyList<TreasuryModel> _treasuries = Array.Empty<TreasuryModel>();
    private IReadOnlyList<TreasuryMethod> _methods = Array.Empty<TreasuryMethod>();
    private Guid _branchId;
    private DateTime? _docDate = DateTime.Today;
    private Partner? _supplier;
    private Guid _treasuryId;
    private Guid _methodId;
    private bool _onCredit;
    private string? _notes;
    private bool _busy;

    private decimal Total => Math.Round(_lines.Sum(l => l.Amount), 2, MidpointRounding.AwayFromZero);

    private IReadOnlyList<Warehouse> BranchWarehouses => _branchId == Guid.Empty
        ? _warehouses
        : _warehouses.Where(w => w.BranchIds.Count == 0 || w.BranchIds.Contains(_branchId))
            .ToList();

    protected override async Task OnInitializedAsync()
    {
        var actor = await UserContext.RequireAsync();
        Branches = await BranchApi.OptionsAsync(actor);
        _branchId = BranchId ?? actor.CurrentBranchId ?? Branches.FirstOrDefault()?.Id ?? Guid.Empty;
        _warehouses = await Lookup.WarehousesAsync(actor);
        _accounts = await Accounts.PostableAsync(actor);
        await ReloadBranchAsync();
        AddLine();
    }

    private async Task ReloadBranchAsync()
    {
        var actor = await UserContext.RequireAsync();
        _costCenters = await CostCenters.OptionsAsync(actor,
            _branchId == Guid.Empty ? actor.CurrentBranchId : _branchId);
        _treasuries = await TreasuryApi.ListTreasuriesAsync(actor, _branchId);
        _methods = await TreasuryApi.ListBranchMethodsAsync(actor, _branchId);
        _treasuryId = _treasuries.FirstOrDefault()?.Id ?? Guid.Empty;
        _methodId = _methods.FirstOrDefault(m => m.TreasuryId == _treasuryId)?.Id ?? Guid.Empty;
    }

    private async Task OnBranchChanged(Guid branchId)
    {
        _branchId = branchId;
        foreach (var line in _lines) line.WarehouseId = null;
        await ReloadBranchAsync();
    }

    private void OnTreasuryChanged(Guid treasuryId)
    {
        _treasuryId = treasuryId;
        _methodId = _methods.FirstOrDefault(m => m.TreasuryId == treasuryId)?.Id ?? Guid.Empty;
    }

    private void OnItemChanged(LineModel line, ItemOption? item)
    {
        line.Item = item;
        if (item is null) { line.WarehouseId = null; return; }
        line.WarehouseId ??= BranchWarehouses.FirstOrDefault()?.Id;
    }

    private void AddLine() => _lines.Add(new LineModel());
    private void Cancel() => MudDialog.Cancel();

    private static string SupplierLabel(Partner? partner) =>
        partner is null ? string.Empty : $"{partner.Code} — {partner.NameAr}";

    private static string AccountLabel(Account? account) =>
        account is null ? string.Empty : $"{account.Code} — {account.NameAr}";

    private static string ItemLabel(ItemOption? item) =>
        item is null ? string.Empty : $"{item.Code} — {item.NameAr}";

    private Task<IEnumerable<Partner>> SearchSuppliers(string? search, CancellationToken ct) =>
        LoadAsync(search, (actor, text) => Lookup.PartnersAsync(actor, PartnerKind.Supplier, text, ct));

    private Task<IEnumerable<ItemOption>> SearchItems(string? search, CancellationToken ct) =>
        LoadAsync(search, (actor, text) => Lookup.ItemsAsync(actor, text, 50, false, ct));

    private Task<IEnumerable<Account>> SearchAccounts(string? search, CancellationToken ct)
    {
        var term = (search ?? string.Empty).Trim();
        var matches = term.Length == 0
            ? _accounts
            : _accounts.Where(a => a.Code.Contains(term, StringComparison.OrdinalIgnoreCase)
                                   || a.NameAr.Contains(term, StringComparison.OrdinalIgnoreCase)
                                   || (a.NameEn ?? "").Contains(term, StringComparison.OrdinalIgnoreCase));
        return Task.FromResult(matches.Take(50));
    }

    private async Task<IEnumerable<T>> LoadAsync<T>(string? search,
        Func<ActorContext, string?, Task<IReadOnlyList<T>>> load)
    {
        var actor = await UserContext.RequireAsync();
        return await load(actor, search);
    }
}
