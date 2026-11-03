namespace Ubp.Core.Models;

/// <summary>صف خدمة محجوزة في شاشة تحصيل نقطة البيع: الإجمالي والمسدد سابقًا والمستحق.</summary>
public sealed class BookingCollectionRow
{
    public Guid BookingId { get; set; }
    public string? BookingNo { get; set; }
    public DateTime BookingDate { get; set; }
    public string? Services { get; set; }
    public decimal TotalAmount { get; set; }
    public decimal PaidAmount { get; set; }
    public decimal RemainingAmount { get; set; }
    public bool Selected { get; set; }
    public decimal Amount { get; set; }
}

public sealed class BookingCollectionLineInput
{
    public Guid BookingId { get; set; }
    public decimal Amount { get; set; }
}

/// <summary>سداد قيمة جلسات محجوزة: سند قبض واحد يُخصَّص على الحجوزات المحددة.</summary>
public sealed class BookingCollectionInput
{
    public Guid BranchId { get; set; }
    public Guid CustomerId { get; set; }
    public Guid TreasuryId { get; set; }
    public Guid TreasuryMethodId { get; set; }
    public DateTime DocDate { get; set; } = DateTime.Today;
    public string? Notes { get; set; }
    public List<BookingCollectionLineInput> Lines { get; set; } = new();
}

public sealed class BookingCollectionResult
{
    public Guid VoucherId { get; set; }
    public string? VoucherNo { get; set; }
    public decimal Total { get; set; }
    public decimal Fee { get; set; }
    public int Bookings { get; set; }
}
