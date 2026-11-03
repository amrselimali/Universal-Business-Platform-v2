namespace Ubp.Core.Models;

/// <summary>إعدادات واتساب المستقلة لكل فرع (Meta Cloud API).</summary>
public sealed class BranchWhatsAppSettings
{
    public Guid BranchId { get; set; }
    public bool IsEnabled { get; set; }
    public string ApiUrl { get; set; } = "https://graph.facebook.com/v20.0";
    public string? PhoneNumberId { get; set; }
    public string? MessagingAccountId { get; set; }
    public string? AccessToken { get; set; }
    public string? WebhookVerifyToken { get; set; }
    public string? AppSecret { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public sealed class BranchWhatsAppSettingsInput
{
    public Guid BranchId { get; set; }
    public bool IsEnabled { get; set; }
    public string ApiUrl { get; set; } = "https://graph.facebook.com/v20.0";
    public string? PhoneNumberId { get; set; }
    public string? MessagingAccountId { get; set; }
    public string? AccessToken { get; set; }
    public string? WebhookVerifyToken { get; set; }
    public string? AppSecret { get; set; }
}
