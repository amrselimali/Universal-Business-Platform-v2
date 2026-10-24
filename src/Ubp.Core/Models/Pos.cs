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

public sealed class PosCheckoutInput
{
    public Guid BranchId { get; set; }
    public Guid CustomerId { get; set; }
    public Guid WarehouseId { get; set; }
    public Guid TreasuryId { get; set; }
    public Guid TreasuryMethodId { get; set; }
    public DateTime DocDate { get; set; } = DateTime.Today;
    public string? Notes { get; set; }
    public List<PosLineInput> Lines { get; set; } = new();
}

public sealed class PosReceipt
{
    public Guid BookingId { get; set; }
    public string? BookingNo { get; set; }
    public Guid DeliveryId { get; set; }
    public string? DeliveryNo { get; set; }
    public Guid InvoiceId { get; set; }
    public string? InvoiceNo { get; set; }
    public Guid VoucherId { get; set; }
    public string? VoucherNo { get; set; }
    public decimal Total { get; set; }
}
