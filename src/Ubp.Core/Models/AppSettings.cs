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
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public int Version { get; set; } = 1;
}
