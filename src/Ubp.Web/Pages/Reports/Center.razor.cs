using System.Text.Json;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using MudBlazor;
using Ubp.Application.Services;
using Ubp.Core.Models;
using Ubp.Core.Results;
using Ubp.Core.Security;

namespace Ubp.Web.Pages.Reports;

public partial class Center
{
    [Inject] private ReportCenterService ReportsCenter { get; set; } = default!;
    [Inject] private WarehouseService Warehouses { get; set; } = default!;
    [Inject] private IJSRuntime JS { get; set; } = default!;

    private IReadOnlyList<ReportDefinition> _reports = Array.Empty<ReportDefinition>();
    private IReadOnlyList<Warehouse> _warehouses = Array.Empty<Warehouse>();
    private IReadOnlyList<SavedReportFilter> _filters = Array.Empty<SavedReportFilter>();
    private Guid? _warehouseId;
    private bool _includeZero;
    private string _filterName = string.Empty;
    private Guid? _selectedFilterId;
    private bool _busy;

    private const string ReportKey = "stock.balances";

    protected override string ScreenCode => ScreenCodes.ReportsCenter;

    protected override async Task OnLoadAsync()
    {
        _reports = await ReportsCenter.ReportsAsync(Actor!);
        _warehouses = await Warehouses.OptionsAsync(Actor!);
        await LoadFiltersAsync();
    }

    private async Task LoadFiltersAsync()
    {
        _filters = await ReportsCenter.FiltersAsync(Actor!, ReportKey);
        _selectedFilterId = null;
    }

    private async Task ExportAsync(ReportExportFormat format)
    {
        if (_busy) return;
        _busy = true;
        try
        {
            var export = await ReportsCenter.ExportStockBalancesAsync(Actor!, format, _warehouseId,
                _includeZero);
            await JS.InvokeVoidAsync("ubpReports.saveFile", export.FileName, export.ContentType,
                Convert.ToBase64String(export.Content));
            Snackbar.Add(T.T("reports.export_done"), Severity.Success);
        }
        catch (AppException ex)
        {
            Snackbar.Add(T.T(ex.Code), Severity.Error);
        }
        finally { _busy = false; }
    }

    private async Task ApplyFilterAsync(Guid? id)
    {
        _selectedFilterId = id;
        var filter = _filters.FirstOrDefault(f => f.Id == id);
        if (filter is null) return;

        try
        {
            using var document = JsonDocument.Parse(filter.FiltersJson);
            _warehouseId = document.RootElement.TryGetProperty("warehouse_id", out var warehouse)
                           && Guid.TryParse(warehouse.GetString(), out var parsed)
                ? parsed
                : null;
            _includeZero = document.RootElement.TryGetProperty("include_zero", out var zero)
                           && bool.TryParse(zero.GetString(), out var include) && include;
            Snackbar.Add(T.T("reports.filter_applied"), Severity.Info);
        }
        catch (JsonException)
        {
            Snackbar.Add(T.T("validation.filter_json_invalid"), Severity.Error);
        }
    }

    private async Task SaveFilterAsync()
    {
        if (_busy) return;
        _busy = true;
        try
        {
            var values = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
            {
                ["warehouse_id"] = _warehouseId?.ToString(),
                ["include_zero"] = _includeZero.ToString().ToLowerInvariant()
            };
            await ReportsCenter.SaveFilterAsync(Actor!, ReportKey, _filterName, values);
            _filterName = string.Empty;
            await LoadFiltersAsync();
            Snackbar.Add(T.T("reports.filter_saved"), Severity.Success);
        }
        catch (AppException ex)
        {
            Snackbar.Add(T.T(ex.Code), Severity.Error);
        }
        finally { _busy = false; }
    }

    private async Task DeleteFilterAsync()
    {
        if (_selectedFilterId is null) return;
        try
        {
            await ReportsCenter.DeleteFilterAsync(Actor!, _selectedFilterId.Value);
            await LoadFiltersAsync();
            Snackbar.Add(T.T("reports.filter_deleted"), Severity.Success);
        }
        catch (AppException ex)
        {
            Snackbar.Add(T.T(ex.Code), Severity.Error);
        }
    }

    private async Task PrintAsync() => await JS.InvokeVoidAsync("print");
}
