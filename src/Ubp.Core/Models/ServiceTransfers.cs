namespace Ubp.Core.Models;

/// <summary>حالات مستند تحويل الخدمات بين العملاء — 0083.</summary>
public static class ServiceTransferStatuses
{
    public const string Draft = "draft";
    public const string Posted = "posted";
}

/// <summary>
/// خدمة/جلسة متاحة قابلة للتحويل للعميل المحوَّل منه: تُعرض «الخدمة — الكمية — سعر الوحدة»
/// داخل خلية/قائمة منسدلة واحدة كما هو مطلوب في شاشة نقطة البيع.
/// </summary>
public sealed class TransferableServiceRow
{
    public Guid BookingLineId { get; set; }
    public Guid ItemId { get; set; }
    public string ItemCode { get; set; } = "";
    public string ItemName { get; set; } = "";
    public Guid UnitId { get; set; }
    public Guid WarehouseId { get; set; }
    public decimal RemainingQty { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal RemainingValue =>
        Math.Round(RemainingQty * UnitPrice, 2, MidpointRounding.AwayFromZero);

    /// <summary>نص الخلية الموحّدة (الخدمة — الكمية — سعر الوحدة).</summary>
    public string Display => $"{ItemName} — {RemainingQty:N2} — {UnitPrice:N2}";
}

public sealed class ServiceTransferLineInput
{
    public Guid FromBookingLineId { get; set; }
    public decimal Quantity { get; set; }
}

public sealed class ServiceTransferInput
{
    public Guid BranchId { get; set; }
    public Guid FromCustomerId { get; set; }
    public Guid ToCustomerId { get; set; }
    public DateTime DocDate { get; set; } = DateTime.Today;
    public string? Notes { get; set; }
    public List<ServiceTransferLineInput> Lines { get; set; } = new();
}

public sealed class ServiceTransferLine
{
    public Guid Id { get; set; }
    public int LineNo { get; set; }
    public Guid FromBookingLineId { get; set; }
    public Guid ItemId { get; set; }
    public string? ItemCode { get; set; }
    public string? ItemName { get; set; }
    public Guid UnitId { get; set; }
    public Guid WarehouseId { get; set; }
    public decimal Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal Amount { get; set; }
}

public sealed class ServiceTransfer
{
    public Guid Id { get; set; }
    public Guid BranchId { get; set; }
    public string? DocNo { get; set; }
    public DateTime DocDate { get; set; }
    public Guid FromCustomerId { get; set; }
    public string? FromCustomerName { get; set; }
    public Guid ToCustomerId { get; set; }
    public string? ToCustomerName { get; set; }
    public string Status { get; set; } = ServiceTransferStatuses.Draft;
    public decimal TotalQuantity { get; set; }
    public decimal TotalAmount { get; set; }
    public Guid? JournalEntryId { get; set; }
    public Guid? TargetBookingId { get; set; }
    public string? Notes { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? PostedAt { get; set; }
    public List<ServiceTransferLine> Lines { get; set; } = new();

    public bool IsDraft => Status == ServiceTransferStatuses.Draft;
    /// <summary>نص موحّد لسطر التحويل في تقرير الوردية: «من ← إلى».</summary>
    public string Display => $"{FromCustomerName} → {ToCustomerName}";
}
