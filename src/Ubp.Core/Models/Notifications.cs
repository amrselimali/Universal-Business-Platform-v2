namespace Ubp.Core.Models;

public static class NotificationSeverities
{
    public const string Info = "INFO";
    public const string Success = "SUCCESS";
    public const string Warning = "WARNING";
    public const string Error = "ERROR";

    public static readonly IReadOnlyList<string> All =
        [Info, Success, Warning, Error];

    public static string Normalize(string? value) =>
        value is not null && All.Contains(value.ToUpperInvariant())
            ? value.ToUpperInvariant() : Info;
}

/// <summary>إشعار داخلي لمستخدم واحد أو لدور (الترحيل 0040).</summary>
public sealed class Notification
{
    public Guid Id { get; set; }
    public Guid? UserId { get; set; }
    public Guid? RoleId { get; set; }
    public string TitleKey { get; set; } = "";
    public string? Body { get; set; }
    public string? LinkUrl { get; set; }
    public string Severity { get; set; } = NotificationSeverities.Info;
    public bool IsRead { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class NotificationInput
{
    public Guid? UserId { get; set; }
    public Guid? RoleId { get; set; }
    /// <summary>مفتاح ترجمة يُعرض للمستخدم (يُترجم في الواجهة).</summary>
    public string TitleKey { get; set; } = "";
    public string? Body { get; set; }
    public string? LinkUrl { get; set; }
    public string Severity { get; set; } = NotificationSeverities.Info;
}
