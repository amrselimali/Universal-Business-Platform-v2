namespace Ubp.Core.Models;

public static class TreasuryTypes
{
    public const string Cash = "cash";
    public const string Bank = "bank";
    public const string Wallet = "wallet";
}

public static class PaymentMethodTypes
{
    public const string Cash = "cash";
    public const string Card = "card";
    public const string Wallet = "wallet";
    public const string Cheque = "cheque";
    public const string Transfer = "transfer";
}

public static class VoucherTypes
{
    public const string Receipt = "receipt";
    public const string Payment = "payment";
    public const string Transfer = "transfer";

    public static bool IsValid(string? value) =>
        value is Receipt or Payment or Transfer;

    /// <summary>بادئة الترقيم في document_sequences لكل نوع سند.</summary>
    public static string SequenceName(string voucherType) => voucherType switch
    {
        Payment => "PAY",
        Transfer => "TRF",
        _ => "RCV"
    };
}

public static class VoucherStatuses
{
    public const string Draft = "draft";
    public const string Posted = "posted";
    public const string Cancelled = "cancelled";

    public static bool IsValid(string? value) =>
        value is Draft or Posted or Cancelled;
}

public static class ShiftStatuses
{
    public const string Open = "open";
    public const string Closed = "closed";
    public const string Reopened = "reopened";
}

public sealed class Treasury
{
    public Guid Id { get; set; }
    public Guid BranchId { get; set; }
    public string Code { get; set; } = "";
    public string NameAr { get; set; } = "";
    public string? NameEn { get; set; }
    public string TreasuryType { get; set; } = TreasuryTypes.Cash;
    public Guid AccountId { get; set; }
    public string? AccountCode { get; set; }
    public string? AccountName { get; set; }
    public decimal OpeningBalance { get; set; }
    public bool IsActive { get; set; } = true;

    public string DisplayName(bool english) =>
        english && !string.IsNullOrWhiteSpace(NameEn) ? NameEn! : NameAr;
}

public sealed class TreasuryInput
{
    public Guid BranchId { get; set; }
    public string Code { get; set; } = "";
    public string NameAr { get; set; } = "";
    public string? NameEn { get; set; }
    public string TreasuryType { get; set; } = TreasuryTypes.Cash;
    public Guid AccountId { get; set; }
    public decimal OpeningBalance { get; set; }
}

public sealed class TreasuryMethod
{
    public Guid Id { get; set; }
    public Guid TreasuryId { get; set; }
    public string Code { get; set; } = "";
    public string NameAr { get; set; } = "";
    public string MethodType { get; set; } = PaymentMethodTypes.Cash;
    public Guid? ClearingAccountId { get; set; }
    public decimal FeePercent { get; set; }
    public decimal FeeFixed { get; set; }
    public bool RequiresReference { get; set; }
    public bool IsActive { get; set; } = true;
}

public sealed class TreasuryMethodInput
{
    public Guid TreasuryId { get; set; }
    public string Code { get; set; } = "";
    public string NameAr { get; set; } = "";
    public string? NameEn { get; set; }
    public string MethodType { get; set; } = PaymentMethodTypes.Cash;
    public Guid? ClearingAccountId { get; set; }
    public decimal FeePercent { get; set; }
    public decimal FeeFixed { get; set; }
    public bool RequiresReference { get; set; }
}

public sealed class Voucher
{
    public Guid Id { get; set; }
    public string VoucherType { get; set; } = VoucherTypes.Receipt;
    public Guid BranchId { get; set; }
    public string? DocNo { get; set; }
    public DateTime DocDate { get; set; }
    public string Status { get; set; } = VoucherStatuses.Draft;
    public string? PartyType { get; set; }
    public string? PartyName { get; set; }
    public Guid? PartyId { get; set; }
    public Guid TreasuryId { get; set; }
    public string? TreasuryCode { get; set; }
    public string? TreasuryName { get; set; }
    public Guid? ToTreasuryId { get; set; }
    public string? ToTreasuryCode { get; set; }
    public string? ToTreasuryName { get; set; }
    public decimal TotalAmount { get; set; }
    public decimal FeeTotal { get; set; }
    public Guid? JournalEntryId { get; set; }
    public string? JournalNo { get; set; }
    public string? Notes { get; set; }
    public DateTimeOffset? PostedAt { get; set; }
    public int Version { get; set; } = 1;
    public List<VoucherLine> Lines { get; set; } = new();

    public bool IsDraft => Status == VoucherStatuses.Draft;
    public bool IsPosted => Status == VoucherStatuses.Posted;

    /// <summary>المبلغ الصافي الذي دخل/خرج فعلياً من الخزينة بعد المصاريف.</summary>
    public decimal NetAmount => Math.Round(TotalAmount - FeeTotal, 2, MidpointRounding.AwayFromZero);
}

public sealed class VoucherLine
{
    public Guid Id { get; set; }
    public int LineNo { get; set; }
    public Guid TreasuryMethodId { get; set; }
    public string MethodCode { get; set; } = "";
    public string MethodName { get; set; } = "";
    public string MethodType { get; set; } = PaymentMethodTypes.Cash;
    public decimal Amount { get; set; }
    public decimal FeeAmount { get; set; }
    public string? Reference { get; set; }
}

public sealed class VoucherAllocation
{
    public Guid Id { get; set; }
    public Guid SourceVoucherId { get; set; }
    public Guid TargetDocumentId { get; set; }
    public string? TargetDocNo { get; set; }
    public DateTime? TargetDate { get; set; }
    public decimal Amount { get; set; }
}

public sealed class VoucherLineInput
{
    public Guid TreasuryMethodId { get; set; }
    public decimal Amount { get; set; }

    /// <summary>null = يُحسب تلقائياً من إعدادات طريقة الدفع (نسبة + مبلغ ثابت).</summary>
    public decimal? FeeAmount { get; set; }
    public string? Reference { get; set; }
}

public sealed class VoucherInput
{
    public string VoucherType { get; set; } = VoucherTypes.Receipt;
    public Guid BranchId { get; set; }
    public DateTime DocDate { get; set; } = DateTime.Today;
    public string? PartyType { get; set; }
    public Guid? PartyId { get; set; }
    public string? PartyName { get; set; }
    public Guid TreasuryId { get; set; }
    public Guid? ToTreasuryId { get; set; }
    public string? Notes { get; set; }
    public List<VoucherLineInput> Lines { get; set; } = new();
}

public sealed class Shift
{
    public Guid Id { get; set; }
    public Guid BranchId { get; set; }
    public Guid TreasuryId { get; set; }
    public string? TreasuryCode { get; set; }
    public string? TreasuryName { get; set; }
    public Guid UserId { get; set; }
    public string? Username { get; set; }
    public string Code { get; set; } = "";
    public DateTimeOffset OpenedAt { get; set; }
    public DateTimeOffset? ClosedAt { get; set; }
    public decimal OpeningCash { get; set; }
    public decimal ExpectedCash { get; set; }
    public decimal? CountedCash { get; set; }
    public decimal? Variance { get; set; }
    public string Status { get; set; } = ShiftStatuses.Open;
    public string? Notes { get; set; }
    public List<ShiftDenomination> Denominations { get; set; } = new();

    public bool IsOpen => Status == ShiftStatuses.Open;
}

public sealed class ShiftInput
{
    public Guid BranchId { get; set; }
    public Guid TreasuryId { get; set; }
    public decimal OpeningCash { get; set; }
    public string? Notes { get; set; }
}

public sealed class ShiftDenominationInput
{
    public decimal Denomination { get; set; }
    public int Count { get; set; }
}

public sealed class ShiftDenomination
{
    public Guid Id { get; set; }
    public decimal Denomination { get; set; }
    public int Count { get; set; }
    public decimal Total => Math.Round(Denomination * Count, 2, MidpointRounding.AwayFromZero);
}
