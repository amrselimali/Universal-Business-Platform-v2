using Ubp.Core.Models;

namespace Ubp.Application.Services;

/// <summary>
/// مصدر واحد لعرض التاريخ/الوقت/الأرقام: يقرأ التنسيقات والمنطقة الزمنية من app_settings،
/// ويحوّل كل ما هو مخزَّن بـ UTC إلى توقيت المستخدم بدل الاعتماد على توقيت السيرفر.
/// </summary>
public interface IDisplayFormatter
{
    TimeZoneInfo Zone { get; }
    DateTimeOffset ToUserTime(DateTimeOffset value);
    string Date(DateTimeOffset? value);
    string DateOnly(DateOnly? value);
    string DateTime(DateTimeOffset? value);
    string Time(DateTimeOffset? value);
    string Number(decimal? value);
    string Decimal(decimal? value, int digits = 2);
}

public sealed class DisplayFormatter : IDisplayFormatter
{
    /// <summary>يُستخدم عندما لا تكون الإعدادات محمّلة بعد (نفس القيم الافتراضية للبذور).</summary>
    private static readonly AppSettings Fallback = new();

    private readonly ISettingsService _settings;
    private string? _zoneName;
    private TimeZoneInfo _zone = TimeZoneInfo.Utc;

    public DisplayFormatter(ISettingsService settings) => _settings = settings;

    private AppSettings Current => _settings.CachedSnapshot ?? Fallback;

    public TimeZoneInfo Zone
    {
        get
        {
            var name = Current.TimeZone;
            if (string.Equals(name, _zoneName, StringComparison.Ordinal)) return _zone;
            _zone = Resolve(name);
            _zoneName = name;
            return _zone;
        }
    }

    public DateTimeOffset ToUserTime(DateTimeOffset value) =>
        TimeZoneInfo.ConvertTime(value.ToUniversalTime(), Zone);

    public string Date(DateTimeOffset? value) =>
        value.HasValue ? ToUserTime(value.Value).ToString(Current.DateFormat) : "—";

    public string DateOnly(DateOnly? value) =>
        value.HasValue ? value.Value.ToString(Current.DateFormat) : "—";

    public string DateTime(DateTimeOffset? value) =>
        value.HasValue
            ? $"{ToUserTime(value.Value).ToString(Current.DateFormat)} " +
              $"{ToUserTime(value.Value).ToString(Current.TimeFormat)}"
            : "—";

    public string Time(DateTimeOffset? value) =>
        value.HasValue ? ToUserTime(value.Value).ToString(Current.TimeFormat) : "—";

    public string Number(decimal? value) =>
        value.HasValue ? value.Value.ToString(Current.NumberFormat) : "—";

    public string Decimal(decimal? value, int digits = 2) =>
        value.HasValue ? Math.Round(value.Value, digits).ToString(Current.NumberFormat) : "—";

    private static TimeZoneInfo Resolve(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return TimeZoneInfo.Utc;
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(name.Trim());
        }
        catch (TimeZoneNotFoundException)
        {
            return TimeZoneInfo.Utc;
        }
        catch (InvalidTimeZoneException)
        {
            return TimeZoneInfo.Utc;
        }
    }
}
