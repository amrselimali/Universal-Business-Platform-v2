namespace Ubp.Core.Models;

public static class SettlementTypes
{
    public const string Refund = "refund";
    public const string CancelQty = "cancel_qty";
    public const string RefundAndCancel = "refund_and_cancel";
    public const string Transfer = "transfer";
    public const string Forfeit = "forfeit";

    public static readonly string[] All =
        { Refund, CancelQty, RefundAndCancel, Transfer, Forfeit };

    public static bool IsValid(string? value) => value is Refund or CancelQty
        or RefundAndCancel or Transfer or Forfeit;

    public static bool HasRefund(string value) => value is Refund or RefundAndCancel;
    public static bool HasCancel(string value) => value is CancelQty or RefundAndCancel;
}

public sealed class Settlement
{
    public Guid Id { get; set; }
    public Guid BranchId { get; set; }
    public string? DocNo { get; set; }
    public DateTime DocDate { get; set; }
    public string Status { get; set; } = CommercialStatuses.Draft;
    public Guid BookingId { get; set; }
    public string? BookingNo { get; set; }
    public string? CustomerName { get; set; }
    public string SettlementType { get; set; } = SettlementTypes.Refund;
    public decimal Amount { get; set; }
    public Guid? TreasuryId { get; set; }
    public string? TreasuryName { get; set; }
    public Guid? TargetBookingId { get; set; }
    public string? Reason { get; set; }
    public Guid? ApprovedBy { get; set; }
    public DateTimeOffset? ApprovedAt { get; set; }
    public Guid? JournalEntryId { get; set; }
    public string? Notes { get; set; }
    public int Version { get; set; }

    public bool IsDraft => Status == CommercialStatuses.Draft;
    public bool IsApproved => ApprovedAt is not null;
}

public sealed class SettlementInput
{
    public Guid BranchId { get; set; }
    public DateTime DocDate { get; set; } = DateTime.Today;
    public Guid BookingId { get; set; }
    public string SettlementType { get; set; } = SettlementTypes.Refund;
    public decimal Amount { get; set; }
    public Guid? TreasuryId { get; set; }
    public Guid? TargetBookingId { get; set; }
    public string? Reason { get; set; }
    public string? Notes { get; set; }
}

public sealed class CommissionRow
{
    public Guid Id { get; set; }
    public Guid EmployeeId { get; set; }
    public string? EmployeeName { get; set; }
    public Guid DocumentId { get; set; }
    public string? DocNo { get; set; }
    public short Basis { get; set; }
    public decimal BaseAmount { get; set; }
    public decimal Rate { get; set; }
    public decimal Amount { get; set; }
    public DateTime PeriodMonth { get; set; }
}

public sealed class CommissionInput
{
    public DateTime Month { get; set; } = new(DateTime.Today.Year, DateTime.Today.Month, 1);
    public decimal Rate { get; set; }
    public short? Basis { get; set; }
}

public sealed class AgingBucketRow
{
    public Guid PartyId { get; set; }
    public string PartyName { get; set; } = "";
    public string? Reference { get; set; }
    public decimal Current { get; set; }
    public decimal Days30 { get; set; }
    public decimal Days60 { get; set; }
    public decimal Days90 { get; set; }
    public decimal Over90 { get; set; }

    public decimal Total => Current + Days30 + Days60 + Days90 + Over90;
}

public sealed class UnpostedDocument
{
    public string Kind { get; set; } = "";
    public Guid Id { get; set; }
    public Guid BranchId { get; set; }
    public string? DocType { get; set; }
    public string? DocNo { get; set; }
    public DateTime DocDate { get; set; }
    public string Status { get; set; } = "";
    public decimal Amount { get; set; }
}

public sealed class ReconciliationCheck
{
    public string Key { get; set; } = "";
    public decimal DocumentValue { get; set; }
    public decimal GlValue { get; set; }

    public decimal Difference => Math.Round(DocumentValue - GlValue, 2,
        MidpointRounding.AwayFromZero);
}
