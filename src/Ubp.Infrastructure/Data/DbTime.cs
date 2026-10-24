namespace Ubp.Infrastructure.Data;

/// <summary>
/// Npgsql 6+ يرفض DateTimeOffset بإزاحة غير صفرية عند الكتابة إلى timestamptz،
/// لذلك تُطبَّع كل الأوقات القادمة من الواجهة إلى UTC قبل الحفظ، ويبقى العرض محلياً.
/// </summary>
public static class DbTime
{
    public static DateTimeOffset? Utc(DateTimeOffset? value) => value?.ToUniversalTime();
    public static DateTimeOffset Utc(DateTimeOffset value) => value.ToUniversalTime();
}
