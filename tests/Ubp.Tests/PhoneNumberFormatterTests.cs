using Ubp.Core;
using Xunit;

namespace Ubp.Tests;

/// <summary>
/// تطبيع أرقام الهاتف لصيغة WhatsApp Cloud API: الرقم المحلي المصري 01001234567
/// يصبح 201001234567 (بدون +)، مع معالجة + و00 والأرقام الدولية.
/// </summary>
public sealed class PhoneNumberFormatterTests
{
    [Theory]
    [InlineData("01001234567", "201001234567")]
    [InlineData(" 0100 123 4567 ", "201001234567")]
    [InlineData("+201001234567", "201001234567")]
    [InlineData("00201001234567", "201001234567")]
    [InlineData("201001234567", "201001234567")]
    [InlineData("1001234567", "201001234567")]
    [InlineData("0223456789", "20223456789")]
    public void Converts_to_international_whatsapp_format(string input, string expected) =>
        Assert.Equal(expected, PhoneNumberFormatter.ToWhatsApp(input));

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("123")]
    [InlineData(null)]
    public void Rejects_empty_or_too_short_numbers(string? input) =>
        Assert.Null(PhoneNumberFormatter.ToWhatsApp(input));
}
