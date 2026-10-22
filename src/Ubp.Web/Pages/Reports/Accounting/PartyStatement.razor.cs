using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using Ubp.Application.Services;
using Ubp.Core.Models;
using Ubp.Core.Paging;
using Ubp.Core.Security;
using Ubp.Infrastructure.Repositories;

namespace Ubp.Web.Pages.Reports.Accounting;

public partial class PartyStatement
{
    [Inject] private AccountingReportService Reports { get; set; } = default!;
    [Inject] private IPartnerRepository Partners { get; set; } = default!;
    [Inject] private IJSRuntime JS { get; set; } = default!;

    private PartyStatementReport? _report;
    private IReadOnlyList<Partner> _parties = Array.Empty<Partner>();
    private char _partyType = 'C';
    private Guid? _partyId;
    private DateTime? _from = new(DateTime.Today.Year, 1, 1);
    private DateTime? _to = DateTime.Today;
    private bool _busy;

    protected override string ScreenCode => ScreenCodes.PartyStatementReport;

    protected override async Task OnLoadAsync()
    {
        await LoadPartiesAsync();
    }

    private async Task LoadPartiesAsync()
    {
        var page = await Partners.ListAsync(_partyType == 'C' ? PartnerKind.Customer : PartnerKind.Supplier,
            new PageRequest { Page = 1, PageSize = PageRequest.MaxPageSize }, null, false,
            Array.Empty<Guid>());
        _parties = page.Items;
        _partyId = _parties.FirstOrDefault()?.Id;
    }

    private async Task SearchAsync() => await RunAsync(async () =>
    {
        if (_partyId is null) return;
        _busy = true;
        try
        {
            _report = await Reports.PartyStatementAsync(Actor!, _partyType, _partyId.Value,
                _from ?? new DateTime(DateTime.Today.Year, 1, 1), _to ?? DateTime.Today, null);
        }
        finally { _busy = false; }
    });

    private async Task PrintAsync() => await JS.InvokeVoidAsync("print");
}
