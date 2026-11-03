namespace Ubp.Core;

/// <summary>
/// تطبيع أرقام الهاتف لصيغة WhatsApp Cloud API: أرقام فقط، بصيغة دولية بدون <c>+</c>.
/// يحوّل الرقم المحلي المصري (01001234567) إلى (201001234567)، ويعالج 00/+ تلقائيًا.
/// </summary>
public static class PhoneNumberFormatter
{
    public const string DefaultCountryCode = "20";

    public static string? ToWhatsApp(string? phone, string countryCode = DefaultCountryCode)
    {
        if (string.IsNullOrWhiteSpace(phone)) return null;

        var trimmed = phone.Trim();
        var digits = new string(trimmed.Where(char.IsDigit).ToArray());
        if (digits.Length == 0) return null;

        var international = trimmed.StartsWith('+') || digits.StartsWith("00");
        if (digits.StartsWith("00")) digits = digits[2..];

        // رقم دولي صريح (+ أو 00): يبقى كما هو.
        if (international)
            return digits.Length >= 8 ? digits : null;

        var code = new string(countryCode.Where(char.IsDigit).ToArray());
        if (code.Length == 0) code = DefaultCountryCode;

        // رقم محلي يبدأ بصفر (01001234567 / 0223456789) → نحذف الصفر ونضيف كود الدولة.
        if (digits.StartsWith('0')) digits = code + digits[1..];
        else if (!digits.StartsWith(code, StringComparison.Ordinal) && digits.Length <= 10)
            // رقم محلي بلا صفر (1001234567) → نضيف كود الدولة.
            digits = code + digits;

        return digits.Length >= 8 ? digits : null;
    }
}
