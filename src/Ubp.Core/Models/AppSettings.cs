namespace Ubp.Core.Models;

public sealed class AppSettings
{
    public Guid Id { get; set; }
    public string CompanyName { get; set; } = "Universal Business Platform";
    public string? LogoUrl { get; set; }
    public string DefaultLanguage { get; set; } = "ar";
    public string DateFormat { get; set; } = "dd/MM/yyyy";
    public string TimeFormat { get; set; } = "HH:mm";
    public string NumberFormat { get; set; } = "#,##0.00";
    public short SessionTimeoutMinutes { get; set; } = 30;
    public short PasswordMinLength { get; set; } = 8;
    public short MaxFailedAttempts { get; set; } = 5;
    public short LockoutMinutes { get; set; } = 15;
    public bool AllowNegativeStock { get; set; }
    public short BookingDefaultDueDays { get; set; } = 30;
    public bool BlockExpiredIssue { get; set; } = true;
    public short DeliveryRequiresCollection { get; set; }
    public bool ReserveStockOnBooking { get; set; } = true;
    public bool AutoInvoiceOnDelivery { get; set; } = true;
    public short InvoiceGroupingMode { get; set; }
    public short AppointmentSlotMinutes { get; set; } = 15;
    public string? PosReceiptFooter { get; set; }
    public bool AutoFollowUpOverdue { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public int Version { get; set; } = 1;

    /// <summary>نسخة مستقلة للتعديل في الواجهة دون المساس بالنسخة المخزنة في الكاش.</summary>
    public AppSettings Clone() => (AppSettings)MemberwiseClone();
}
