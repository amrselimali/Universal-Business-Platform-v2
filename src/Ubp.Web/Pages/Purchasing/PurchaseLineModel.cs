namespace Ubp.Web.Pages.Purchasing;

/// <summary>سطر إدخال في شاشة فاتورة/مرتجع الشراء (نموذج واجهة فقط).</summary>
public sealed class PurchaseLineModel
{
    public Guid ItemId { get; set; }
    public Guid UnitId { get; set; }
    public decimal Quantity { get; set; } = 1;
    public decimal Price { get; set; }
    public decimal Discount { get; set; }
    public Guid? TaxId { get; set; }
    public string? LotNumber { get; set; }
    public DateTime? Expiry { get; set; }
}
