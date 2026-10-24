namespace Ubp.Core.Models;

public static class BookingStatuses
{
    public const string Draft = "draft";
    public const string Confirmed = "confirmed";
    public const string PartiallyDelivered = "partially_delivered";
    public const string Completed = "completed";
    public const string Cancelled = "cancelled";
    public const string Expired = "expired";

    /// <summary>قابل للتعديل قبل بدء التنفيذ فقط.</summary>
    public static bool IsEditable(string? status) => status is Draft or Confirmed;
}

public static class BookingSources
{
    public const string Manual = "manual";
    public const string Pos = "pos";
}

public static class AppointmentStatuses
{
    public const string Scheduled = "scheduled";
    public const string Confirmed = "confirmed";
    public const string InProgress = "in_progress";
    public const string Done = "done";
    public const string Cancelled = "cancelled";
    public const string NoShow = "no_show";

    public static bool IsValid(string? value) =>
        value is Scheduled or Confirmed or InProgress or Done or Cancelled or NoShow;
}

public static class DeliveryStatuses
{
    public const string Draft = "draft";
    public const string Posted = "posted";
    public const string Cancelled = "cancelled";
}

public sealed class Booking
{
    public Guid Id { get; set; }
    public Guid BranchId { get; set; }
    public string? DocNo { get; set; }
    public DateTime BookingDate { get; set; }
    public string Status { get; set; } = BookingStatuses.Draft;
    public Guid CustomerId { get; set; }
    public string? CustomerName { get; set; }
    public string Source { get; set; } = BookingSources.Manual;
    public DateTime? DueDate { get; set; }
    public DateTimeOffset? ExpiresAt { get; set; }
    public Guid? AssignedEmployeeId { get; set; }
    public string? EmployeeName { get; set; }
    public Guid? RelatedBookingId { get; set; }
    public decimal TotalAmount { get; set; }
    public decimal DepositAmount { get; set; }
    public string? Notes { get; set; }
    public int Version { get; set; } = 1;
    public DateTimeOffset CreatedAt { get; set; }
    public List<BookingLine> Lines { get; set; } = new();
    public List<BookingAssignment> Assignments { get; set; } = new();

    public bool IsDraft => Status == BookingStatuses.Draft;
    public bool IsConfirmed => Status == BookingStatuses.Confirmed;
    public bool IsClosed => Status is BookingStatuses.Completed or BookingStatuses.Cancelled;
    public decimal DeliveredRatio => TotalAmount == 0 ? 0 : DeliveredAmount / TotalAmount;
    public decimal DeliveredAmount => Lines.Sum(l => l.TotalAmount == 0 ? 0 :
        Math.Round(l.TotalAmount * (l.DeliveredQty / l.Quantity), 2, MidpointRounding.AwayFromZero));
}

public sealed class BookingLine
{
    public Guid Id { get; set; }
    public int LineNo { get; set; }
    public Guid ItemId { get; set; }
    public string? ItemCode { get; set; }
    public string? ItemName { get; set; }
    public Guid UnitId { get; set; }
    public string? UnitName { get; set; }
    public Guid WarehouseId { get; set; }
    public decimal Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal DiscountPercent { get; set; }
    public Guid? TaxId { get; set; }
    public decimal TaxRate { get; set; }
    public decimal NetAmount { get; set; }
    public decimal TaxAmount { get; set; }
    public decimal TotalAmount { get; set; }
    public decimal DeliveredQty { get; set; }
    public decimal InvoicedQty { get; set; }
    public decimal CancelledQty { get; set; }
    public decimal ReservedQty { get; set; }

    public decimal RemainingQty => Math.Round(Quantity - DeliveredQty - CancelledQty, 4,
        MidpointRounding.AwayFromZero);
}

public sealed class BookingAssignment
{
    public Guid Id { get; set; }
    public Guid EmployeeId { get; set; }
    public string? EmployeeName { get; set; }
    public string Role { get; set; } = "owner";
    public DateTimeOffset AssignedAt { get; set; }
    public DateTimeOffset? UnassignedAt { get; set; }
}

public sealed class BookingFilter
{
    public Guid? BranchId { get; set; }
    public string? Status { get; set; }
    public Guid? CustomerId { get; set; }
    public string? Search { get; set; }
}

public sealed class BookingInput
{
    public Guid BranchId { get; set; }
    public DateTime BookingDate { get; set; } = DateTime.Today;
    public Guid CustomerId { get; set; }
    public DateTime? DueDate { get; set; }
    public DateTimeOffset? ExpiresAt { get; set; }
    public Guid? AssignedEmployeeId { get; set; }
    public Guid? RelatedBookingId { get; set; }
    public string? Notes { get; set; }
    public List<BookingLineInput> Lines { get; set; } = new();
}

public sealed class BookingLineInput
{
    public Guid ItemId { get; set; }
    public Guid UnitId { get; set; }
    public Guid WarehouseId { get; set; }
    public decimal Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal DiscountPercent { get; set; }
    public Guid? TaxId { get; set; }
}

public sealed class Appointment
{
    public Guid Id { get; set; }
    public Guid BranchId { get; set; }
    public Guid EmployeeId { get; set; }
    public string? EmployeeName { get; set; }
    public Guid? BookingId { get; set; }
    public string? BookingNo { get; set; }
    public Guid? CustomerId { get; set; }
    public string? CustomerName { get; set; }
    public DateTimeOffset StartsAt { get; set; }
    public DateTimeOffset EndsAt { get; set; }
    public string Status { get; set; } = AppointmentStatuses.Scheduled;
    public string? Notes { get; set; }
    public int Version { get; set; } = 1;

    public bool IsCancelled => Status == AppointmentStatuses.Cancelled;
}

public sealed class AppointmentInput
{
    public Guid BranchId { get; set; }
    public Guid EmployeeId { get; set; }
    public Guid? BookingId { get; set; }
    public Guid? CustomerId { get; set; }
    public DateTimeOffset StartsAt { get; set; }
    public DateTimeOffset EndsAt { get; set; }
    public string? Notes { get; set; }
}

public sealed class DeliveryNote
{
    public Guid Id { get; set; }
    public Guid BranchId { get; set; }
    public string? DocNo { get; set; }
    public DateTime DocDate { get; set; }
    public string Status { get; set; } = DeliveryStatuses.Draft;
    public Guid BookingId { get; set; }
    public string? BookingNo { get; set; }
    public Guid WarehouseId { get; set; }
    public string? WarehouseName { get; set; }
    public Guid? StockDocumentId { get; set; }
    public Guid? JournalEntryId { get; set; }
    public string? Notes { get; set; }
    public int Version { get; set; } = 1;
    public List<DeliveryNoteLine> Lines { get; set; } = new();

    public bool IsDraft => Status == DeliveryStatuses.Draft;
    public decimal CostTotal => Lines.Sum(l => l.CostTotal);
}

public sealed class DeliveryNoteLine
{
    public Guid Id { get; set; }
    public int LineNo { get; set; }
    public Guid BookingLineId { get; set; }
    public Guid ItemId { get; set; }
    public string? ItemName { get; set; }
    public Guid UnitId { get; set; }
    public decimal Quantity { get; set; }
    public decimal UnitCost { get; set; }
    public decimal CostTotal { get; set; }
}

public sealed class DeliveryInput
{
    public Guid BookingId { get; set; }
    public Guid BranchId { get; set; }
    public DateTime DocDate { get; set; } = DateTime.Today;
    public Guid WarehouseId { get; set; }
    public string? Notes { get; set; }
    public List<DeliveryLineInput> Lines { get; set; } = new();
}

public sealed class DeliveryLineInput
{
    public Guid BookingLineId { get; set; }
    public decimal Quantity { get; set; }
}
