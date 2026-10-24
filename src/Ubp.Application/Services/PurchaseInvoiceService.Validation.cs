using Ubp.Core.Models;
using Ubp.Core.Results;
using Ubp.Core.Security;

namespace Ubp.Application.Services;

public sealed partial class PurchaseInvoiceService
{
    public async Task<IReadOnlyList<SupplierAgingRow>> SupplierAgingAsync(ActorContext actor,
        Guid? branchId, DateTime asOf, CancellationToken ct = default)
    {
        var allowed = await _permissions.HasAsync(actor, ScreenCodes.PurchaseInvoices,
                            ActionCodes.View, ct)
                      || await _permissions.HasAsync(actor, ScreenCodes.PurchaseAging,
                            ActionCodes.View, ct);
        if (!allowed) throw new ForbiddenException();
        return await _documents.SupplierAgingAsync(branchId, asOf, ct);
    }

    /// <summary>تحقق الحقول قبل الحفظ (المسودة تُقبل بلا توازن، والترحيل يفحص الفترة والحسابات).</summary>
    private static void Validate(CommercialDocumentInput input)
    {
        if (!CommercialDocTypes.IsPurchase(input.DocType))
            throw new ValidationAppException("validation.not_supported_document");
        if (input.BranchId == Guid.Empty)
            throw new ValidationAppException("validation.branch_required");
        if (input.SupplierId is null)
            throw new ValidationAppException("validation.supplier_required");
        if (input.WarehouseId is null)
            throw new ValidationAppException("validation.warehouse_required");
        if (input.Lines.Count == 0)
            throw new ValidationAppException("validation.document_has_no_lines");
        if (CommercialDocTypes.IsReturn(input.DocType) && input.OriginalDocumentId is null)
            throw new ValidationAppException("validation.return_requires_original");

        foreach (var line in input.Lines)
        {
            if (line.ItemId == Guid.Empty || line.UnitId == Guid.Empty)
                throw new ValidationAppException("validation.required_fields");
            if (line.Quantity <= 0)
                throw new ValidationAppException("validation.line_amount");
            if (line.UnitPrice < 0)
                throw new ValidationAppException("validation.price_negative");
            if (line.DiscountPercent is < 0 or > 100)
                throw new ValidationAppException("validation.line_amount");
        }
    }
}
