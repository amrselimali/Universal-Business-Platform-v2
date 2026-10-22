using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using Ubp.Application.Services;
using Ubp.Core.Models;
using Ubp.Core.Security;

namespace Ubp.Web.Pages.Reports.Accounting;

public partial class CostCenterReport
{
    [Inject] private AccountingReportService Reports { get; set; } = default!;
    [Inject] private IJSRuntime JS { get; set; } = default!;

    private IReadOnlyList<CostCenterAnalysisRow> _rows = Array.Empty<CostCenterAnalysisRow>();
    private DateTime? _from = new(DateTime.Today.Year, 1, 1);
    private DateTime? _to = DateTime.Today;
    private bool _busy;

    protected override string ScreenCode => ScreenCodes.CostCenterReport;

    protected override async Task OnLoadAsync() => await SearchAsync();

    private async Task SearchAsync() => await RunAsync(async () =>
    {
        _busy = true;
        try
        {
            _rows = await Reports.CostCenterAsync(Actor!,
                _from ?? new DateTime(DateTime.Today.Year, 1, 1), _to ?? DateTime.Today, null);
        }
        finally { _busy = false; }
    });

    private async Task PrintAsync() => await JS.InvokeVoidAsync("print");
}
