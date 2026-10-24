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
    public string Status { get; set; } = "draft";
    public string? PartyName { get; set; }
    public Guid? PartyId { get; set; }
    public Guid TreasuryId { get; set; }
    public Guid? ToTreasuryId { get; set; }
    public decimal TotalAmount { get; set; }
    public decimal FeeTotal { get; set; }
    public Guid? JournalEntryId { get; set; }
    public string? Notes { get; set; }
    public int Version { get; set; } = 1;
}

public sealed class VoucherLineInput
{
    public Guid TreasuryMethodId { get; set; }
    public decimal Amount { get; set; }
    public decimal FeeAmount { get; set; }
    public string? Reference { get; set; }
}

public sealed class VoucherInput
{
    public string VoucherType { get; set; } = VoucherTypes.Receipt;
    public Guid BranchId { get; set; }
    public DateTime DocDate { get; set; } = DateTime.Today;
    public char? PartyType { get; set; }
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
    public Guid UserId { get; set; }
    public string Code { get; set; } = "";
    public DateTimeOffset OpenedAt { get; set; }
    public DateTimeOffset? ClosedAt { get; set; }
    public decimal OpeningCash { get; set; }
    public decimal ExpectedCash { get; set; }
    public decimal? CountedCash { get; set; }
    public decimal? Variance { get; set; }
    public string Status { get; set; } = "open";
}

public sealed class ShiftInput
{
    public Guid BranchId { get; set; }
    public Guid TreasuryId { get; set; }
    public decimal OpeningCash { get; set; }
}
