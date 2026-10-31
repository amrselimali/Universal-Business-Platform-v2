using Microsoft.AspNetCore.Components;
using MudBlazor;
using Ubp.Application.Services;
using Ubp.Core.Models;
using Ubp.Core.Paging;
using Ubp.Core.Results;
using Ubp.Core.Security;

namespace Ubp.Web.Pages.Assets;

public partial class AssetManagement
{
    [Inject] private BranchService BranchService { get; set; } = default!;
    [Inject] private IDialogService DialogService { get; set; } = default!;

    private IReadOnlyList<FixedAsset> _items = Array.Empty<FixedAsset>();
    private string? _search;
    private bool _meterOnly;
    private bool _busy;

    protected override string ScreenCode => ScreenCodes.Assets;

    protected override Task OnLoadAsync() => ReloadAsync();

    private async Task ReloadAsync() => await RunAsync(async () =>
    {
        var page = await AssetsApi.ListAsync(Actor!, new PageRequest
        {
            PageSize = PageRequest.MaxPageSize, Search = _search
        }, _search, null, _meterOnly);
        _items = page.Items;
    });

    private async Task OnMeterOnlyChanged(bool value)
    {
        _meterOnly = value;
        await ReloadAsync();
    }

    private async Task CreateAsync()
    {
        var categories = (await AssetsApi.CategoriesAsync(Actor!)).ToList();
        if (categories.Count == 0)
        {
            Snackbar.Add(T.T("assets.no_categories"), Severity.Warning);
            return;
        }

        var branches = await BranchService.OptionsAsync(Actor!);
        var parameters = new DialogParameters
        {
            ["Categories"] = categories, ["Branches"] = branches
        };
        var dialog = await DialogService.ShowAsync<AssetDialog>(T.T("assets.new"), parameters);
        var result = await dialog.Result;
        if (result is null || result.Canceled || result.Data is not AssetInput input) return;

        await RunAsync(async () =>
        {
            await AssetsApi.CreateAsync(Actor!, input);
            await ReloadAsync();
        });
    }

    private async Task CreateCategoryAsync()
    {
        var dialog = await DialogService.ShowAsync<CategoryDialog>(T.T("assets.new_category"));
        var result = await dialog.Result;
        if (result is null || result.Canceled || result.Data is not AssetCategoryInput input) return;

        await RunAsync(async () =>
        {
            await AssetsApi.CreateCategoryAsync(Actor!, input);
            Snackbar.Add(T.T("assets.category_saved"), Severity.Success);
            await ReloadAsync();
        });
    }

    private async Task AddReadingAsync(FixedAsset asset)
    {
        var dialog = await DialogService.ShowAsync<ReadingDialog>(T.T("assets.add_reading"));
        var result = await dialog.Result;
        if (result is null || result.Canceled || result.Data is not decimal value) return;

        await RunAsync(async () =>
        {
            await AssetsApi.AddReadingAsync(Actor!, asset.Id, value);
            await ReloadAsync();
        });
        Snackbar.Add(T.T("assets.reading_saved"), Severity.Success);
    }

    private async Task DisposeAsync(FixedAsset asset)
    {
        var dialog = await DialogService.ShowAsync<DisposeDialog>(T.T("assets.dispose"));
        var result = await dialog.Result;
        if (result is null || result.Canceled || result.Data is not decimal amount) return;

        await RunAsync(async () =>
        {
            var outcome = await AssetsApi.DisposeAsync(Actor!, asset.Id, amount, DateTime.Today);
            await ReloadAsync();
            Snackbar.Add($"{T.T("assets.dispose_done")} ({Fmt.Decimal(outcome.Gain)} / {Fmt.Decimal(outcome.Loss)})",
                Severity.Success);
        });
    }

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
            var result = await AssetsApi.RunDepreciationAsync(Actor!, DateTime.Today, branchId.Value);
            Snackbar.Add($"{T.T("assets.depreciation_done")} ({Fmt.Decimal(result.Total)})",
                Severity.Success);
            await ReloadAsync();
        }
        catch (AppException ex) { Snackbar.Add(T.T(ex.Code), Severity.Error); }
        finally { _busy = false; }
    }
}
