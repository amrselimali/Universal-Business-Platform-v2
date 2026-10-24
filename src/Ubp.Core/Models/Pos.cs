namespace Ubp.Core.Models;

public sealed class PosLineInput
{
    public Guid ItemId { get; set; }
    public Guid UnitId { get; set; }
    public decimal Quantity { get; set; } = 1;
    public decimal UnitPrice { get; set; }
    public decimal DiscountPercent { get; set; }
    public Guid? TaxId { get; set; }
}

/// <summary>سطر تحصيل: خزينة + طريقة دفع + مبلغ — يجوز تعددها ولا يُشترط وجودها.</summary>
public sealed class PosPaymentInput
{
    public Guid TreasuryId { get; set; }
    public Guid TreasuryMethodId { get; set; }
    public decimal Amount { get; set; }
    public string? Reference { get; set; }
}

public sealed class PosCheckoutInput
{
    public Guid BranchId { get; set; }
    public Guid CustomerId { get; set; }
    public Guid WarehouseId { get; set; }
    public DateTime DocDate { get; set; } = DateTime.Today;
    public string? Notes { get; set; }
    public List<PosLineInput> Lines { get; set; } = new();
    public List<PosPaymentInput> Payments { get; set; } = new();
}

public sealed class PosPaymentResult
{
    public Guid VoucherId { get; set; }
    public string? VoucherNo { get; set; }
    public Guid TreasuryId { get; set; }
    public decimal Amount { get; set; }
    public decimal Fee { get; set; }
    public int Lines { get; set; }
}

public sealed class PosReceipt
{
    public Guid BookingId { get; set; }
    public string? BookingNo { get; set; }
    public decimal Total { get; set; }
    public decimal TaxTotal { get; set; }
    public decimal Paid { get; set; }
    public decimal FeeTotal { get; set; }
    public List<PosPaymentResult> Vouchers { get; set; } = new();
}

/// <summary>حجز موعد: خدمة + وقت من/إلى فقط، بلا أسعار أو كميات.</summary>
public sealed class PosAppointmentInput
{
    public Guid BranchId { get; set; }
    public Guid CustomerId { get; set; }
    public Guid WarehouseId { get; set; }
    public Guid ItemId { get; set; }
    public DateTimeOffset StartsAt { get; set; }
    public DateTimeOffset EndsAt { get; set; }
    public string? Notes { get; set; }
}
