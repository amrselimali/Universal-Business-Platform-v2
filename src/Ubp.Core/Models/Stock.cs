namespace Ubp.Core.Models;

public static class StockDocumentTypes
{
    public const string Receipt = "RECEIPT";
    public const string Issue = "ISSUE";
    public const string Transfer = "TRANSFER";
    public const string Adjustment = "ADJUSTMENT";
    public const string Opening = "OPENING";
    public const string Reversal = "REVERSAL";

    public static readonly string[] All =
        { Receipt, Issue, Transfer, Adjustment, Opening, Reversal };
}

public static class StockStatuses
{
    public const string Draft = "DRAFT";
    public const string Posted = "POSTED";
    public const string PartiallyReceived = "PARTIALLY_RECEIVED";
    public const string Received = "RECEIVED";
    public const string Cancelled = "CANCELLED";
}

public sealed class StockDocument
{
    public Guid Id { get; set; }
    public string? DocNo { get; set; }
    public string DocType { get; set; } = StockDocumentTypes.Receipt;
    public DateTime DocDate { get; set; }
    public Guid BranchId { get; set; }
    public string? BranchName { get; set; }
    public Guid WarehouseId { get; set; }
    public string? WarehouseName { get; set; }
    public Guid? ToBranchId { get; set; }
    public string? ToBranchName { get; set; }
    public Guid? ToWarehouseId { get; set; }
    public string? ToWarehouseName { get; set; }
    public string Status { get; set; } = StockStatuses.Draft;
    public string? Notes { get; set; }
    public decimal TotalCost { get; set; }
    public Guid? ReversalOfId { get; set; }
    public bool IsReversed { get; set; }
    public Guid? StocktakeId { get; set; }
    public DateTimeOffset? PostedAt { get; set; }
    public string? PostedByName { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public int Version { get; set; } = 1;
    public int LinesCount { get; set; }
}

public sealed class StockDocumentLine
{
    public Guid Id { get; set; }
    public Guid DocumentId { get; set; }
    public short LineNo { get; set; }
    public string Direction { get; set; } = "IN";
    public Guid ItemId { get; set; }
    public string? ItemCode { get; set; }
    public string? ItemName { get; set; }
    public Guid UnitId { get; set; }
    public string? UnitName { get; set; }
    public decimal ConversionFactor { get; set; } = 1;
    public decimal Quantity { get; set; }
    public decimal BaseQuantity { get; set; }
    public decimal UnitCost { get; set; }
    public decimal TotalCost { get; set; }
    public decimal ReceivedQuantity { get; set; }
    public decimal RemainingQuantity => Math.Max(BaseQuantity - ReceivedQuantity, 0);
    public string? Notes { get; set; }
}

public sealed class StockDocumentInput
{
    public string DocType { get; set; } = StockDocumentTypes.Receipt;
    public DateTime DocDate { get; set; } = DateTime.Today;
    public Guid BranchId { get; set; }
    public Guid WarehouseId { get; set; }
    public Guid? ToBranchId { get; set; }
    public Guid? ToWarehouseId { get; set; }
    public string? Notes { get; set; }
    public List<StockDocumentLineInput> Lines { get; set; } = new();
}

public sealed class StockDocumentLineInput
{
    public Guid ItemId { get; set; }
    public Guid UnitId { get; set; }
    public decimal ConversionFactor { get; set; } = 1;
    public decimal Quantity { get; set; }
    public decimal UnitCost { get; set; }
    public string? Notes { get; set; }
    public string? Direction { get; set; }
}

public sealed class StockDocumentDetail
{
    public StockDocument Document { get; set; } = new();
    public IReadOnlyList<StockDocumentLine> Lines { get; set; } = Array.Empty<StockDocumentLine>();
    public IReadOnlyList<TransferReceipt> Receipts { get; set; } = Array.Empty<TransferReceipt>();
}

public sealed class StockDocumentFilter
{
    public string? DocType { get; set; }
    public string? Status { get; set; }
    public DateTime? From { get; set; }
    public DateTime? To { get; set; }
    public Guid? WarehouseId { get; set; }
}

public sealed class TransferReceipt
{
    public Guid Id { get; set; }
    public Guid DocumentId { get; set; }
    public DateTimeOffset ReceivedAt { get; set; }
    public string? ReceivedByName { get; set; }
    public string? Notes { get; set; }
    public IReadOnlyList<TransferReceiptLine> Lines { get; set; } = Array.Empty<TransferReceiptLine>();
}

public sealed class TransferReceiptLine
{
    public Guid Id { get; set; }
    public Guid ReceiptId { get; set; }
    public Guid DocumentLineId { get; set; }
    public string? ItemName { get; set; }
    public decimal Quantity { get; set; }
}

public sealed class TransferReceiptInput
{
    public Guid DocumentId { get; set; }
    public string? Notes { get; set; }
    public List<TransferReceiptLineInput> Lines { get; set; } = new();
}

public sealed class TransferReceiptLineInput
{
    public Guid DocumentLineId { get; set; }
    public decimal Quantity { get; set; }
}

public sealed class StockMovement
{
    public long Id { get; set; }
    public Guid DocumentId { get; set; }
    public string? DocNo { get; set; }
    public string? DocType { get; set; }
    public Guid ItemId { get; set; }
    public string? ItemCode { get; set; }
    public string? ItemName { get; set; }
    public Guid WarehouseId { get; set; }
    public string? WarehouseName { get; set; }
    public string? BranchName { get; set; }
    public string Direction { get; set; } = "IN";
    public decimal Quantity { get; set; }
    public decimal UnitCost { get; set; }
    public decimal TotalCost { get; set; }
    public DateTime MovementDate { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class StockBalance
{
    public Guid ItemId { get; set; }
    public string ItemCode { get; set; } = "";
    public string ItemName { get; set; } = "";
    public Guid WarehouseId { get; set; }
    public string WarehouseName { get; set; } = "";
    public decimal OnHand { get; set; }
    public decimal AverageCost { get; set; }
    public decimal InventoryValue { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public sealed class Stocktake
{
    public Guid Id { get; set; }
    public string Code { get; set; } = "";
    public Guid BranchId { get; set; }
    public string? BranchName { get; set; }
    public Guid WarehouseId { get; set; }
    public string? WarehouseName { get; set; }
    public string Status { get; set; } = "DRAFT";
    public string? Notes { get; set; }
    public DateTimeOffset? PostedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public int Version { get; set; } = 1;
    public int LinesCount { get; set; }
    public decimal TotalDifference { get; set; }
    public Guid? DocumentId { get; set; }
}

public sealed class StocktakeLine
{
    public Guid Id { get; set; }
    public Guid StocktakeId { get; set; }
    public Guid ItemId { get; set; }
    public string? ItemCode { get; set; }
    public string? ItemName { get; set; }
    public decimal BookQty { get; set; }
    public decimal CountedQty { get; set; }
    public decimal Difference { get; set; }
    public decimal UnitCost { get; set; }
}

public sealed class StocktakeDetail
{
    public Stocktake Header { get; set; } = new();
    public IReadOnlyList<StocktakeLine> Lines { get; set; } = Array.Empty<StocktakeLine>();
}

public sealed class StocktakeInput
{
    public Guid BranchId { get; set; }
    public Guid WarehouseId { get; set; }
    public string? Notes { get; set; }
}

public sealed class StocktakeLineInput
{
    public Guid ItemId { get; set; }
    public decimal CountedQty { get; set; }
    public decimal UnitCost { get; set; }
}
