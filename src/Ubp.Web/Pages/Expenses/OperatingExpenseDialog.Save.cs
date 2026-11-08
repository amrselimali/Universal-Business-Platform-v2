using MudBlazor;
using Ubp.Core.Models;
using Ubp.Core.Results;

namespace Ubp.Web.Pages.Expenses;

public partial class OperatingExpenseDialog
{
    private Task SaveDraftAsync() => SaveAsync(post: false);
    private Task SaveAndPostAsync() => SaveAsync(post: true);

    private async Task SaveAsync(bool post)
    {
        if (_docDate is null || _branchId == Guid.Empty)
        {
            Snackbar.Add(T.T("validation.required_fields"), Severity.Warning);
            return;
        }
        if (_supplier is null)
        {
            Snackbar.Add(T.T("validation.expense_supplier_required"), Severity.Warning);
            return;
        }
        if (!_onCredit && _treasuryId == Guid.Empty)
        {
            Snackbar.Add(T.T("validation.expense_payment_incomplete"), Severity.Warning);
            return;
        }

        var lines = _lines.Where(l => l.Quantity > 0 && l.UnitPrice > 0).ToList();
        if (lines.Count == 0)
        {
            Snackbar.Add(T.T("validation.document_has_no_lines"), Severity.Warning);
            return;
        }

        _busy = true;
        try
        {
            var actor = await UserContext.RequireAsync();
            var created = await Expenses.CreateAsync(actor, new OperatingExpenseInput
            {
                BranchId = _branchId, DocDate = _docDate.Value, SupplierId = _supplier.Id,
                TreasuryId = _onCredit ? null : _treasuryId,
                Lines = lines.Select(l => new OperatingExpenseLineInput
                {
                    ItemId = l.Item?.Id,
                    UnitId = l.Item?.BaseUnitId,
                    WarehouseId = l.Item is null ? null : l.WarehouseId,
                    CostCenterId = l.CostCenterId, Description = l.Description,
                    Quantity = l.Quantity, UnitPrice = l.UnitPrice
                }).ToList()
            });

            if (post)
            {
                try { await Expenses.PostAsync(actor, created.Id); }
                catch (AppException ex)
                {
                    // المستند محفوظ كمسودة؛ نُظهر سبب فشل الترحيل ونغلق الحوار لعرضه في القائمة.
                    Snackbar.Add(T.T(ex.Code), Severity.Error);
                    MudDialog.Close(DialogResult.Ok(created));
                    return;
                }
            }

            MudDialog.Close(DialogResult.Ok(created));
        }
        catch (AppException ex) { Snackbar.Add(T.T(ex.Code), Severity.Error); }
        finally { _busy = false; }
    }
}
