using Microsoft.AspNetCore.Components;
using MudBlazor;
using Ubp.Application.Services;
using Ubp.Core.Models;
using Ubp.Core.Results;
using Ubp.Web.Services;

namespace Ubp.Web.Pages.Inventory;

public partial class StocktakeDialog
{
    [CascadingParameter] private IMudDialogInstance MudDialog { get; set; } = default!;
    [Inject] private StocktakeService Stocktakes { get; set; } = default!;
    [Inject] private BranchService Branches { get; set; } = default!;
    [Inject] private WarehouseService Warehouses { get; set; } = default!;
    [Inject] private UserContext UserContext { get; set; } = default!;
    [Inject] private UiCulture T { get; set; } = default!;
    [Inject] private ISnackbar Snackbar { get; set; } = default!;

    private MudForm _form = default!;
    private IReadOnlyList<Branch> _branches = Array.Empty<Branch>();
    private IReadOnlyList<Warehouse> _warehouses = Array.Empty<Warehouse>();
    private Guid _branchId;
    private Guid _warehouseId;
    private string? _notes;
    private bool _busy;

    protected override async Task OnInitializedAsync()
    {
        var actor = await UserContext.RequireAsync();
        _branches = await Branches.OptionsAsync(actor);
        _warehouses = await Warehouses.OptionsAsync(actor);
        _branchId = actor.CurrentBranchId ?? _branches.FirstOrDefault()?.Id ?? Guid.Empty;
        _warehouseId = _warehouses.FirstOrDefault(w => w.BranchIds.Contains(_branchId))?.Id ?? Guid.Empty;
    }

    private void OnBranchChanged() =>
        _warehouseId = _warehouses.FirstOrDefault(w => w.BranchIds.Contains(_branchId))?.Id ?? Guid.Empty;

    private void Cancel() => MudDialog.Cancel();

    private async Task SaveAsync()
    {
        await _form.Validate();
        if (!_form.IsValid) return;
        _busy = true;
        try
        {
            var actor = await UserContext.RequireAsync();
            var created = await Stocktakes.CreateAsync(actor, new StocktakeInput
            {
                BranchId = _branchId, WarehouseId = _warehouseId, Notes = _notes
            });
            MudDialog.Close(DialogResult.Ok(created));
        }
        catch (AppException ex) { Snackbar.Add(T.T(ex.Code), Severity.Error); }
        finally { _busy = false; }
    }
}
