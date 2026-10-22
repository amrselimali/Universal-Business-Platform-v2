using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using Ubp.Application.Services;
using Ubp.Core.Models;
using Ubp.Core.Security;

namespace Ubp.Web.Pages.Reports.Accounting;

public partial class StockVsGl
{
    [Inject] private AccountingReportService Reports { get; set; } = default!;
    [Inject] private IJSRuntime JS { get; set; } = default!;

    private IReadOnlyList<StockVsGlRow> _rows = Array.Empty<StockVsGlRow>();
    private bool _busy;
    private bool _allMatched = true;

    protected override string ScreenCode => ScreenCodes.StockVsGlReport;

    protected override async Task OnLoadAsync() => await ReloadAsync();

    private async Task ReloadAsync() => await RunAsync(async () =>
    {
        _busy = true;
        try
        {
            _rows = await Reports.StockVsGlAsync(Actor!);
            _allMatched = _rows.All(r => r.IsMatched);
        }
        finally { _busy = false; }
    });

    private async Task PrintAsync() => await JS.InvokeVoidAsync("print");
}
