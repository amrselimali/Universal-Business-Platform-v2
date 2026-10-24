using Microsoft.AspNetCore.Components;
using MudBlazor;
using Ubp.Core.Models;

namespace Ubp.Web.Pages.Accounting;

public partial class CostDistributionDialog
{
    [CascadingParameter] private IMudDialogInstance MudDialog { get; set; } = default!;
    [Parameter] public IReadOnlyList<CostCenter> CostCenters { get; set; } = Array.Empty<CostCenter>();
    [Parameter] public bool IsEnglish { get; set; }

    private readonly List<DistributionRow> _rows = new();
    private decimal Total => _rows.Sum(r => r.Percentage);
    private bool IsValid =>
        _rows.Count > 0 && Math.Abs(Total - 100m) <= 0.01m &&
        _rows.All(r => r.CostCenterId != Guid.Empty);

    protected override void OnInitialized()
    {
        AddRow();
        AddRow();
    }

    private void AddRow() => _rows.Add(new DistributionRow
    {
        CostCenterId = CostCenters.FirstOrDefault()?.Id ?? Guid.Empty
    });

    private void Remove(DistributionRow row)
    {
        _rows.Remove(row);
        if (_rows.Count == 0) AddRow();
    }

    private void Confirm() => MudDialog.Close(DialogResult.Ok(_rows
        .Select(r => new CostDistributionLine
        {
            CostCenterId = r.CostCenterId, Percentage = r.Percentage
        }).ToList()));

    private sealed class DistributionRow
    {
        public Guid CostCenterId { get; set; }
        public decimal Percentage { get; set; }
    }
}
