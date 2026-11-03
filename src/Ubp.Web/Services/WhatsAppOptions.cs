namespace Ubp.Web.Services;

/// <summary>إعدادات واتساب العامة (احتياطية عند عدم تفعيل إعدادات الفرع).</summary>
public sealed class WhatsAppOptions
{
    public bool Enabled { get; set; }
    public string ApiUrl { get; set; } = "https://graph.facebook.com/v20.0";
    public string? PhoneNumberId { get; set; }
    public string? Token { get; set; }
}
