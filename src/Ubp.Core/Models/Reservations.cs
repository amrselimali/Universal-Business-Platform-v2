namespace Ubp.Core.Models;

public static class ReservationAdjustmentKinds
{
    public const string ToAdvance = "TO_ADVANCE";
    public const string Release = "RELEASE";
    public const string Extend = "EXTEND";
}

/// <summary>سطر من عرض الحجوزات: ما حُجز وما استُهلك وما بقِي غير مستخدم (الترحيل 0047).</summary>
public sealed class ReservationRow
{
    public Guid BookingId { get; set; }
    public Guid BranchId { get; set; }
    public Guid CustomerId { get; set; }
    public string BookingStatus { get; set; } = "";
    public string? BookingNo { get; set; }
    public decimal DepositAmount { get; set; }
    public DateTimeOffset? ExpiresAt { get; set; }
    public Guid BookingLineId { get; set; }
    public string CustomerName { get; set; } = "";
    public Guid ItemId { get; set; }
    public string ItemCode { get; set; } = "";
    public string ItemName { get; set; } = "";
    public Guid WarehouseId { get; set; }
    public string WarehouseName { get; set; } = "";
    public decimal BookedQty { get; set; }
    public decimal ReservedQty { get; set; }
    public decimal ConsumedQty { get; set; }
    public decimal RemainingQty { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal RemainingValue { get; set; }
}

/// <summary>ناتج تحويل المتبقي إلى دفعة مقدمة: سند قبض مرحَّل + المبلغ المحوَّل.</summary>
public sealed record ReservationConversion(Guid VoucherId, decimal Amount, decimal Quantity,
    int LinesConverted);
