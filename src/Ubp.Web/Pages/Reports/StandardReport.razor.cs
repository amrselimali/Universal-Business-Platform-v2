using Microsoft.AspNetCore.Components;
using MudBlazor;
using Microsoft.JSInterop;
using Ubp.Application.Services;
using Ubp.Core.Models;
using Ubp.Core.Results;
using Ubp.Core.Security;

namespace Ubp.Web.Pages.Reports;

public partial class StandardReport
{
    [Parameter] public string Key { get; set; } = "";

    private ReportTable? _table;
    private IReadOnlyList<Branch> _branches = Array.Empty<Branch>();
    private IReadOnlyList<Guid> _branchIds = Array.Empty<Guid>();
    private DateTime? _from = new(DateTime.Today.Year, 1, 1);
    private DateTime? _to = DateTime.Today;
    private bool _busy;

    private string Title => T.T(ReportCatalog.Find(Key)?.TitleKey ?? "screen.reports_center");

    protected override string ScreenCode => ScreenCodes.ReportsCenter;

    protected override async Task OnLoadAsync()
    {
        _branches = await Branches.OptionsAsync(Actor!);
        await LoadAsync();
    }

    private async Task LoadAsync() => await RunAsync(async () =>
    {
        _busy = true;
        try
        {
            _table = await ReportsSvc.RunAsync(Actor!, Key, new ReportFilter(
                _from ?? new DateTime(DateTime.Today.Year, 1, 1), _to ?? DateTime.Today, _branchIds));
        }
        catch (AppException ex) { Snackbar.Add(T.T(ex.Code), Severity.Error); }
        finally { _busy = false; }
    });

    private async Task OnBranchScope(IReadOnlyList<Guid> branchIds)
    {
        _branchIds = branchIds;
        await LoadAsync();
    }

    private async Task ExportAsync(ReportExportFormat format)
    {
        if (_table is null) return;
        var headers = _table.Columns.Select(c => T.T(c.TitleKey)).ToArray();
        var rows = _table.Rows.Select(r => (IReadOnlyList<string?>)r.ToArray()).ToList();
        var bytes = format == ReportExportFormat.Csv
            ? ReportExporter.Csv(headers, rows)
            : ReportExporter.Xlsx(Title, headers, rows);
        var name = $"{Key}-{DateTime.UtcNow:yyyyMMdd-HHmm}" +
                   (format == ReportExportFormat.Csv ? ".csv" : ".xlsx");
        var type = format == ReportExportFormat.Csv
            ? "text/csv; charset=utf-8"
            : "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
        await JS.InvokeVoidAsync("ubpReports.saveFile", name, type,
            Convert.ToBase64String(bytes));
    }

    private string Cell(string? value) =>
        string.IsNullOrWhiteSpace(value) ? "—" :
        value == "sales" ? T.T("report.sales") :
        value == "purchase" ? T.T("report.purchase") : value;
}
