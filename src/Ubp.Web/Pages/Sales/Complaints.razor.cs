using Microsoft.AspNetCore.Components;
using MudBlazor;
using Ubp.Application.Services;
using Ubp.Core.Models;
using Ubp.Core.Security;

namespace Ubp.Web.Pages.Sales;

public partial class Complaints
{
    [Inject] private ComplaintService ComplaintsApi { get; set; } = default!;
    [Inject] private LookupService Lookup { get; set; } = default!;
    [Inject] private MasterDataService MasterData { get; set; } = default!;
    [Inject] private IDisplayFormatter Fmt { get; set; } = default!;

    private IReadOnlyList<Complaint> _items = Array.Empty<Complaint>();
    private IReadOnlyList<Partner> _customers = Array.Empty<Partner>();
    private IReadOnlyList<Employee> _employees = Array.Empty<Employee>();
    private IReadOnlyList<ItemOption> _services = Array.Empty<ItemOption>();
    private ComplaintInput _input = new();
    private DateTime? _date = DateTime.Today;
    private DateTime? _from = DateTime.Today.AddDays(-30);
    private DateTime? _to = DateTime.Today;
    private string? _status;
    private bool _busy;

    protected override string ScreenCode => ScreenCodes.Complaints;

    protected override async Task OnLoadAsync()
    {
        var branchId = Actor!.CurrentBranchId
                       ?? Actor.AllowedBranchIds.FirstOrDefault();
        _input = new ComplaintInput { BranchId = branchId, DocDate = DateTime.Today };
        _customers = await Lookup.PartnersAsync(Actor, PartnerKind.Customer);
        _employees = await MasterData.EmployeeOptionsAsync(Actor!);
        _services = await Lookup.ItemsAsync(Actor, null, 100, false);
        await ReloadAsync();
    }

    private async Task ReloadAsync()
    {
        var branchId = _input.BranchId;
        if (branchId == Guid.Empty) return;
        _items = await ComplaintsApi.ListAsync(Actor!, branchId, _from, _to, _status);
    }

    private async Task OnStatusChanged(string? status)
    {
        _status = status;
        await ReloadAsync();
    }

    private async Task AddAsync() => await RunAsync(async () =>
    {
        if (_input.BranchId == Guid.Empty || string.IsNullOrWhiteSpace(_input.Description))
        {
            Snackbar.Add(T.T("validation.required_fields"), Severity.Warning);
            return;
        }
        _busy = true;
        try
        {
            _input.DocDate = _date ?? DateTime.Today;
            await ComplaintsApi.CreateAsync(Actor!, _input);
            _input.Description = "";
            _input.CustomerId = null;
            _input.EmployeeId = null;
            _input.ItemId = null;
            await ReloadAsync();
            Snackbar.Add(T.T("common.saved"), Severity.Success);
        }
        finally { _busy = false; }
    });

    private async Task CloseAsync(Complaint complaint) => await RunAsync(async () =>
    {
        _busy = true;
        try
        {
            await ComplaintsApi.CloseAsync(Actor!, complaint.Id);
            await ReloadAsync();
        }
        finally { _busy = false; }
    });
}