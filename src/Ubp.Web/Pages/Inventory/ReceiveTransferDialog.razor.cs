using Microsoft.AspNetCore.Components;
using MudBlazor;
using Ubp.Application.Services;
using Ubp.Core.Models;
using Ubp.Core.Results;
using Ubp.Web.Services;

namespace Ubp.Web.Pages.Inventory;

public partial class ReceiveTransferDialog
{
    [CascadingParameter] private IMudDialogInstance MudDialog { get; set; } = default!;
    [Parameter] public Guid DocumentId { get; set; }
    [Inject] private StockDocumentService Documents { get; set; } = default!;
    [Inject] private UserContext UserContext { get; set; } = default!;
    [Inject] private UiCulture T { get; set; } = default!;
    [Inject] private ISnackbar Snackbar { get; set; } = default!;

    private StockDocumentDetail? _detail;
    private readonly Dictionary<Guid, decimal> _quantities = new();
    private string? _notes;
    private bool _busy;

    protected override async Task OnInitializedAsync()
    {
        var actor = await UserContext.RequireAsync();
        _detail = await Documents.GetDetailAsync(actor, DocumentId);
        foreach (var line in _detail.Lines)
            _quantities[line.Id] = line.RemainingQuantity;
    }

    private decimal Qty(Guid lineId) => _quantities.TryGetValue(lineId, out var value) ? value : 0;
    private void SetQty(Guid lineId, decimal value) => _quantities[lineId] = value;
    private void Cancel() => MudDialog.Cancel();

    private async Task SaveAsync()
    {
        _busy = true;
        try
        {
            var actor = await UserContext.RequireAsync();
            var input = new TransferReceiptInput
            {
                DocumentId = DocumentId,
                Notes = _notes,
                Lines = _detail!.Lines
                    .Where(l => Qty(l.Id) > 0)
                    .Select(l => new TransferReceiptLineInput { DocumentLineId = l.Id, Quantity = Qty(l.Id) })
                    .ToList()
            };
            await Documents.ReceiveAsync(actor, input);
            MudDialog.Close(DialogResult.Ok(true));
        }
        catch (AppException ex) { Snackbar.Add(T.T(ex.Code), Severity.Error); }
        finally { _busy = false; }
    }
}
