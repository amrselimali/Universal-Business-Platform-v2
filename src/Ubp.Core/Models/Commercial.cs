namespace Ubp.Core.Models;

public static class CommercialDocTypes
{
    public const string PurchaseInvoice = "purchase_invoice";
    public const string PurchaseReturn = "purchase_return";
    public const string SalesInvoice = "sales_invoice";
    public const string SalesReturn = "sales_return";

    public static readonly string[] All =
        { PurchaseInvoice, PurchaseReturn, SalesInvoice, SalesReturn };

    public static bool IsPurchase(string docType) =>
        docType is PurchaseInvoice or PurchaseReturn;

    public static bool IsReturn(string docType) =>
        docType is PurchaseReturn or SalesReturn;

    public static string SequenceName(string docType) => docType switch
    {
        PurchaseInvoice => "PURCHASE_INVOICE",
        PurchaseReturn => "PURCHASE_RETURN",
        SalesInvoice => "SALES_INVOICE",
        SalesReturn => "SALES_RETURN",
        _ => "COMMERCIAL_DOC"
    };
}

public static class CommercialStatuses
{
    public const string Draft = "draft";
    public const string Posted = "posted";
    public const string Cancelled = "cancelled";
}

public sealed class CommercialDocument
{
    public Guid Id { get; set; }
    public string DocType { get; set; } = CommercialDocTypes.PurchaseInvoice;
    public string? DocNo { get; set; }
    public DateTime DocDate { get; set; }
    public string Status { get; set; } = CommercialStatuses.Draft;
    public Guid BranchId { get; set; }
    public string? BranchName { get; set; }
    public Guid? WarehouseId { get; set; }
    public string? WarehouseName { get; set; }

    public Guid? SupplierId { get; set; }
    public Guid? CustomerId { get; set; }
    public string? PartyName { get; set; }
    public Guid? PaymentTermsId { get; set; }
    public DateTime? DueDate { get; set; }

    public decimal Subtotal { get; set; }
    public decimal DiscountTotal { get; set; }
    public decimal TaxTotal { get; set; }
    public decimal Total { get; set; }
    public decimal AllocatedTotal { get; set; }
    public decimal Balance => Total - AllocatedTotal;

    public Guid? JournalEntryId { get; set; }
    public string? EntryNo { get; set; }
    public Guid? StockDocumentId { get; set; }
    public Guid? OriginalDocumentId { get; set; }
    public string? Notes { get; set; }

    public DateTimeOffset? PostedAt { get; set; }
    public string? PostedByName { get; set; }
    public DateTimeOffset? ReversedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public int Version { get; set; } = 1;
    public int LinesCount { get; set; }

    public bool IsDraft => Status == CommercialStatuses.Draft;
    public bool IsPosted => Status == CommercialStatuses.Posted;
}

public sealed class CommercialDocumentLine
{
    public Guid Id { get; set; }
    public Guid DocumentId { get; set; }
    public int LineNo { get; set; }
    public Guid ItemId { get; set; }
    public string? ItemCode { get; set; }
    public string? ItemName { get; set; }
    public Guid UnitId { get; set; }
    public string? UnitName { get; set; }
    public decimal ConversionFactor { get; set; } = 1;
    public decimal Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal DiscountPercent { get; set; }
    public decimal DiscountAmount { get; set; }
    public decimal NetAmount { get; set; }
    public Guid? TaxId { get; set; }
    public decimal TaxRate { get; set; }
    public decimal TaxAmount { get; set; }
    public decimal TotalAmount { get; set; }
    public decimal? UnitCost { get; set; }
    public decimal? CostTotal { get; set; }
    public string? LotNumber { get; set; }
    public DateTime? ExpiryDate { get; set; }
    public decimal ReturnedQty { get; set; }
    public string? Description { get; set; }
}

public sealed class CommercialDocumentLineInput
{
    public Guid ItemId { get; set; }
    public Guid UnitId { get; set; }
    public decimal ConversionFactor { get; set; } = 1;
    public decimal Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal DiscountPercent { get; set; }
    public Guid? TaxId { get; set; }
    public string? LotNumber { get; set; }
    public DateTime? ExpiryDate { get; set; }
    public string? Description { get; set; }
}

public sealed class CommercialDocumentInput
{
    public string DocType { get; set; } = CommercialDocTypes.PurchaseInvoice;
    public Guid BranchId { get; set; }
    public Guid? WarehouseId { get; set; }
    public DateTime DocDate { get; set; } = DateTime.Today;
    public Guid? SupplierId { get; set; }
    public Guid? CustomerId { get; set; }
    public Guid? PaymentTermsId { get; set; }
    public DateTime? DueDate { get; set; }
    public Guid? OriginalDocumentId { get; set; }
    public string? Notes { get; set; }
    public List<CommercialDocumentLineInput> Lines { get; set; } = new();
}

public sealed class CommercialDocumentDetail
{
    public CommercialDocument Header { get; set; } = new();
    public IReadOnlyList<CommercialDocumentLine> Lines { get; set; } =
        Array.Empty<CommercialDocumentLine>();
}

public sealed class CommercialDocFilter
{
    public string? DocType { get; set; }
    public string? Search { get; set; }
    public Guid? BranchId { get; set; }
    public Guid? SupplierId { get; set; }
    public Guid? CustomerId { get; set; }
    public string? Status { get; set; }
 
    public DateTime? From { get; set; }
    public DateTime? To { get; set; }
}

public sealed class SupplierAgingRow
{
    public Guid SupplierId { get; set; }
    public string SupplierName { get; set; } = "";
    public decimal Current { get; set; }
    public decimal Days30 { get; set; }
    public decimal Days60 { get; set; }
    public decimal Days90 { get; set; }
    public decimal Over90 { get; set; }
    public decimal Total => Current + Days30 + Days60 + Days90 + Over90;
}
