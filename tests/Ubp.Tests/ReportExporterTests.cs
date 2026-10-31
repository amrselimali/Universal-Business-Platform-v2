using System.IO.Compression;
using System.Text;
using Ubp.Application.Services;
using Xunit;

namespace Ubp.Tests;

/// <summary>
/// مصدّر التقارير: CSV بترميز UTF-8 مع BOM وتهريب RFC4180، وXLSX حزمة OOXML صحيحة
/// مبنية بـ System.IO.Compression فقط (بلا أي حزمة خارجية — X-01).
/// </summary>
public sealed class ReportExporterTests
{
    [Fact]
    public void Csv_starts_with_a_bom_and_escapes_special_characters()
    {
        var bytes = ReportExporter.Csv(["code", "name"],
            [new[] { "A-1", "صنف, \"مميز\"\nسطر" }]);

        Assert.Equal(0xEF, bytes[0]);
        var text = Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);
        Assert.StartsWith("code,name", text);
        Assert.Contains("\"صنف, \"\"مميز\"\"\nسطر\"", text);
    }

    [Fact]
    public void Xlsx_is_a_valid_zip_with_the_required_parts()
    {
        var bytes = ReportExporter.Xlsx("Stock balances",
            ["code", "qty"], [new[] { "IT-1", "12.5" }]);

        using var buffer = new MemoryStream(bytes);
        using var zip = new ZipArchive(buffer, ZipArchiveMode.Read);
        var names = zip.Entries.Select(e => e.FullName).ToHashSet();

        Assert.Contains("[Content_Types].xml", names);
        Assert.Contains("_rels/.rels", names);
        Assert.Contains("xl/workbook.xml", names);
        Assert.Contains("xl/worksheets/sheet1.xml", names);
    }

    [Fact]
    public void Xlsx_writes_inline_strings_numbers_and_escapes_xml()
    {
        var bytes = ReportExporter.Xlsx("Report",
            ["code", "name"], [new[] { "IT-1", "صنف <A> & \"B\"" }]);
        var sheet = ReadEntry(bytes, "xl/worksheets/sheet1.xml");

        Assert.Contains("<c r=\"A1\" t=\"inlineStr\"><is><t xml:space=\"preserve\">code</t>", sheet);
        Assert.Contains("IT-1", sheet);
        Assert.Contains("&lt;A&gt; &amp; &quot;B&quot;", sheet);
        Assert.DoesNotContain("<A>", sheet);
    }

    [Fact]
    public void Numeric_values_are_written_as_numbers_not_text()
    {
        var bytes = ReportExporter.Xlsx("Report", ["qty"], [new[] { "12.5" }]);
        var sheet = ReadEntry(bytes, "xl/worksheets/sheet1.xml");
        Assert.Contains("<c r=\"A2\"><v>12.5</v></c>", sheet);
    }

    [Theory]
    [InlineData(1, "A")]
    [InlineData(26, "Z")]
    [InlineData(27, "AA")]
    [InlineData(28, "AB")]
    [InlineData(52, "AZ")]
    [InlineData(53, "BA")]
    public void Column_names_follow_the_spreadsheet_convention(int index, string expected)
    {
        Assert.Equal(expected, ReportExporter.ColumnName(index));
    }

    [Fact]
    public void Sheet_names_are_sanitized_and_truncated()
    {
        var bytes = ReportExporter.Xlsx("Bad/Name:[*?] And A Very Long Sheet Name Here",
            ["a"], [new[] { "1" }]);
        var workbook = ReadEntry(bytes, "xl/workbook.xml");
        var name = System.Text.RegularExpressions.Regex.Match(workbook, "name=\"([^\"]*)\"").Groups[1].Value;

        Assert.StartsWith("BadName And", name);
        Assert.True(name.Length <= 31, $"sheet name too long: {name.Length}");
        Assert.DoesNotContain('[', name);
        Assert.DoesNotContain(':', name);
        Assert.DoesNotContain('/', name);
    }

    private static string ReadEntry(byte[] bytes, string path)
    {
        using var buffer = new MemoryStream(bytes);
        using var zip = new ZipArchive(buffer, ZipArchiveMode.Read);
        using var stream = zip.GetEntry(path)!.Open();
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
