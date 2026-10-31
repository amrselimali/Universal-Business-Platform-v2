using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using MudBlazor;
using Ubp.Application.Services;
using Ubp.Core.Models;
using Ubp.Core.Results;
using Ubp.Core.Security;

namespace Ubp.Web.Pages.Admin;

public partial class ImportTool
{
    [Inject] private BranchService BranchService { get; set; } = default!;

    private IReadOnlyList<Branch> _branches = Array.Empty<Branch>();
    private IReadOnlyList<ImportJob> _jobs = Array.Empty<ImportJob>();
    private OpeningBalanceImportResult? _preview;
    private string? _csv;
    private string? _fileName;
    private Guid? _branchId;
    private DateTime? _entryDate = DateTime.Today;
    private bool _busy;

    protected override string ScreenCode => ScreenCodes.ImportTool;

    protected override async Task OnLoadAsync()
    {
        _branches = await BranchService.OptionsAsync(Actor!);
        _branchId = Actor!.CurrentBranchId ?? _branches.FirstOrDefault()?.Id;
        _jobs = await Importer.JobsAsync(Actor!);
    }

    private async Task OnFileAsync(InputFileChangeEventArgs args)
    {
        var file = args.File;
        if (file.Size > ImportService.MaxCsvLength)
        {
            Snackbar.Add(T.T("validation.csv_too_large"), Severity.Error);
            return;
        }

        await using var stream = file.OpenReadStream(ImportService.MaxCsvLength);
        using var reader = new StreamReader(stream);
        _csv = await reader.ReadToEndAsync();
        _fileName = file.Name;
        _preview = null;
    }

    private async Task PreviewAsync()
    {
        if (_csv is null) return;
        _busy = true;
        try
        {
            _preview = await Importer.PreviewAsync(Actor!, _csv);
        }
        catch (AppException ex) { Snackbar.Add(T.T(ex.Code), Severity.Error); }
        finally { _busy = false; }
    }

    private async Task ExecuteAsync()
    {
        if (_csv is null || _branchId is null) return;
        _busy = true;
        try
        {
            var outcome = await Importer.ExecuteAsync(Actor!, _csv, _fileName, _branchId.Value,
                _entryDate ?? DateTime.Today);
            Snackbar.Add(T.T(outcome.Success ? "import.executed" : "import.failed"),
                outcome.Success ? Severity.Success : Severity.Error);
            _preview = null;
            _csv = null;
            _fileName = null;
            _jobs = await Importer.JobsAsync(Actor!);
        }
        catch (AppException ex) { Snackbar.Add(T.T(ex.Code), Severity.Error); }
        finally { _busy = false; }
    }

    private void OnBranchChanged(Guid? branchId) => _branchId = branchId;
}
