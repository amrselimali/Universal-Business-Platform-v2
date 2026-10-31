using Microsoft.AspNetCore.Components;
using MudBlazor;
using Ubp.Application.Services;
using Ubp.Core.Results;
using Ubp.Core.Security;
using Ubp.Core.Models;

namespace Ubp.Web.Pages.Assets;

public partial class Depreciation
{
    private IReadOnlyList<DepreciationRunRow> _runs = Array.Empty<DepreciationRunRow>();
    private DateTime? _month = DateTime.Today;
    private bool _busy;

    protected override string ScreenCode => ScreenCodes.AssetDepreciation;

    protected override Task OnLoadAsync() => ReloadAsync();

    private async Task ReloadAsync() => await RunAsync(async () =>
    {
        _runs = await AssetsApi.RunsAsync(Actor!, Actor!.CurrentBranchId);
    });

    private async Task RunDepreciationAsync()
    {
        if (_busy) return;
        var branchId = Actor!.CurrentBranchId;
        if (branchId is null)
        {
            Snackbar.Add(T.T("validation.branch_required"), Severity.Warning);
            return;
        }

        _busy = true;
        try
        {
            var result = await AssetsApi.RunDepreciationAsync(Actor!,
                _month ?? DateTime.Today, branchId.Value);
            Snackbar.Add($"{T.T("assets.depreciation_done")} ({Fmt.Decimal(result.Total)})",
                Severity.Success);
            await ReloadAsync();
        }
        catch (AppException ex) { Snackbar.Add(T.T(ex.Code), Severity.Error); }
        finally { _busy = false; }
    }
}
