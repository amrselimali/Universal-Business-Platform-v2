using Microsoft.AspNetCore.Components;
using MudBlazor;
using Ubp.Application.Services;
using Ubp.Core.Models;
using Ubp.Core.Results;
using Ubp.Web.Services;

namespace Ubp.Web.Pages.Inventory;

public partial class StocktakeCountDialog
{
    [CascadingParameter] private IMudDialogInstance MudDialog { get; set; } = default!;
    [Parameter] public Guid Id { get; set; }
    [Parameter] public string Code { get; set; } = "";
    [Inject] private StocktakeService Stocktakes { get; set; } = default!;
    [Inject] private UserContext UserContext { get; set; } = default!;
    [Inject] private UiCulture T { get; set; } = default!;
    [Inject] private ISnackbar Snackbar { get; set; } = default!;

    private StocktakeDetail? _detail;
    private readonly Dictionary<Guid, decimal> _counted = new();
    private readonly Dictionary<Guid, decimal> _costs = new();
    private readonly Dictionary<Guid, string?> _reasons = new();
    private bool _busy;

    protected override async Task OnInitializedAsync()
    {
        var actor = await UserContext.RequireAsync();
        _detail = await Stocktakes.GetDetailAsync(actor, Id);
        Code = _detail.Header.Code;
        foreach (var line in _detail.Lines)
        {
            _counted[line.ItemId] = line.CountedQty;
            _costs[line.ItemId] = line.UnitCost;
            _reasons[line.ItemId] = line.Reason;
        }
    }

    private decimal Counted(Guid itemId) => _counted.TryGetValue(itemId, out var value) ? value : 0;
    private void SetCounted(Guid itemId, decimal value) => _counted[itemId] = value;
    private string? Reason(Guid itemId) => _reasons.TryGetValue(itemId, out var value) ? value : null;
    private void SetReason(Guid itemId, string? value) => _reasons[itemId] = value;
    private void Cancel() => MudDialog.Cancel();

    private async Task SaveAsync()
    {
        _busy = true;
        try
        {
            var actor = await UserContext.RequireAsync();
            var lines = _counted.Select(pair => new StocktakeLineInput
            {
                ItemId = pair.Key, CountedQty = pair.Value,
                UnitCost = _costs.TryGetValue(pair.Key, out var cost) ? cost : 0,
                Reason = _reasons.TryGetValue(pair.Key, out var reason) ? reason : null
            }).ToList();
            await Stocktakes.UpdateLinesAsync(actor, Id, lines);
            MudDialog.Close(DialogResult.Ok(true));
        }
        catch (AppException ex) { Snackbar.Add(T.T(ex.Code), Severity.Error); }
        finally { _busy = false; }
    }
}
