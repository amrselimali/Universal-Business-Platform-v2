using System.Globalization;
using System.IO.Compression;
using System.Security;
using System.Text;

namespace Ubp.Application.Services;

/// <summary>صيغة تصدير مدعومة (CSV/XLSX) بلا أي حزمة خارجية.</summary>
public enum ReportExportFormat
{
    Csv = 0,
    Xlsx = 1
}

public sealed record ReportExport(string FileName, string ContentType, byte[] Content);

/// <summary>
/// تصدير التقارير: CSV بترميز UTF-8 مع BOM (يفتح عربياً في Excel)،
/// وXLSX مبني يدوياً بتنسيق OOXML أدنى داخل ZIP (System.IO.Compression فقط — X-01).
/// </summary>
public static class ReportExporter
{
    private const string SheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

    public static byte[] Csv(IReadOnlyList<string> headers,
        IEnumerable<IReadOnlyList<string?>> rows)
    {
        var builder = new StringBuilder();
        builder.AppendLine(string.Join(',', headers.Select(Escape)));
        foreach (var row in rows)
            builder.AppendLine(string.Join(',', row.Select(Escape)));
        return Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(builder.ToString()))
            .ToArray();
    }

    public static byte[] Xlsx(string sheetName, IReadOnlyList<string> headers,
        IEnumerable<IReadOnlyList<string?>> rows)
    {
        using var buffer = new MemoryStream();
        using (var zip = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            Add(zip, "[Content_Types].xml", ContentTypes);
            Add(zip, "_rels/.rels", RootRels);
            Add(zip, "xl/workbook.xml", Workbook(SafeSheetName(sheetName)));
            Add(zip, "xl/_rels/workbook.xml.rels", WorkbookRels);
            Add(zip, "xl/styles.xml", Styles);
            Add(zip, "xl/worksheets/sheet1.xml", Sheet(headers, rows));
        }
        return buffer.ToArray();
    }

    /// <summary>اسم عمود Excel (1 = A، 27 = AA).</summary>
    public static string ColumnName(int index)
    {
        if (index < 1) throw new ArgumentOutOfRangeException(nameof(index));
        var name = string.Empty;
        while (index > 0)
        {
            var remainder = (index - 1) % 26;
            name = (char)('A' + remainder) + name;
            index = (index - 1) / 26;
        }
        return name;
    }

    private static string Escape(string? value)
    {
        var text = value ?? string.Empty;
        return text.Contains('"') || text.Contains(',') || text.Contains('\n') || text.Contains('\r')
            ? "\"" + text.Replace("\"", "\"\"") + "\""
            : text;
    }

    private static void Add(ZipArchive zip, string path, string content)
    {
        var entry = zip.CreateEntry(path, CompressionLevel.Optimal);
        using var stream = entry.Open();
        using var writer = new StreamWriter(stream, new UTF8Encoding(false));
        writer.Write(content);
    }

    private static string SafeSheetName(string name)
    {
        var cleaned = new string(name.Where(c => c is not ('[' or ']' or ':' or '*' or '?' or '/'
            or '\\')).ToArray()).Trim();
        if (string.IsNullOrWhiteSpace(cleaned)) cleaned = "Report";
        return cleaned.Length > 31 ? cleaned[..31] : cleaned;
    }

    private static string Sheet(IReadOnlyList<string> headers,
        IEnumerable<IReadOnlyList<string?>> rows)
    {
        var builder = new StringBuilder();
        builder.Append("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>");
        builder.Append($"<worksheet xmlns=\"{SheetNs}\"><sheetData>");
        AppendRow(builder, 1, headers);
        var index = 1;
        foreach (var row in rows) AppendRow(builder, ++index, row);
        builder.Append("</sheetData></worksheet>");
        return builder.ToString();
    }

    private static void AppendRow(StringBuilder builder, int rowIndex, IEnumerable<string?> values)
    {
        builder.Append($"<row r=\"{rowIndex}\">");
        var column = 0;
        foreach (var value in values)
        {
            column++;
            var reference = ColumnName(column) + rowIndex;
            if (string.IsNullOrEmpty(value)) continue;
            if (rowIndex > 1 && decimal.TryParse(value, NumberStyles.Any,
                    CultureInfo.InvariantCulture, out var number))
            {
                builder.Append($"<c r=\"{reference}\"><v>")
                    .Append(number.ToString(CultureInfo.InvariantCulture))
                    .Append("</v></c>");
                continue;
            }
            builder.Append($"<c r=\"{reference}\" t=\"inlineStr\"><is><t xml:space=\"preserve\">")
                .Append(SecurityElement.Escape(value))
                .Append("</t></is></c>");
        }
        builder.Append("</row>");
    }

    private const string ContentTypes = """
        <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
        <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
          <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
          <Default Extension="xml" ContentType="application/xml"/>
          <Override PartName="/xl/workbook.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml"/>
          <Override PartName="/xl/worksheets/sheet1.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"/>
          <Override PartName="/xl/styles.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml"/>
        </Types>
        """;

    private const string RootRels = """
        <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
        <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
          <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="xl/workbook.xml"/>
        </Relationships>
        """;

    private static string Workbook(string sheetName) =>
        "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
        $"<workbook xmlns=\"{SheetNs}\" " +
        "xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\">" +
        $"<sheets><sheet name=\"{SecurityElement.Escape(sheetName)}\" sheetId=\"1\" r:id=\"rId1\"/>" +
        "</sheets></workbook>";

    private const string WorkbookRels = """
        <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
        <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
          <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet" Target="worksheets/sheet1.xml"/>
          <Relationship Id="rId2" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles" Target="styles.xml"/>
        </Relationships>
        """;

    private const string Styles = """
        <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
        <styleSheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
          <fonts count="1"><font><sz val="11"/><name val="Calibri"/></font></fonts>
          <fills count="1"><fill><patternFill patternType="none"/></fill></fills>
          <borders count="1"><border/></borders>
          <cellStyleXfs count="1"><xf numFmtId="0" fontId="0" fillId="0" borderId="0"/></cellStyleXfs>
          <cellXfs count="1"><xf numFmtId="0" fontId="0" fillId="0" borderId="0" xfId="0"/></cellXfs>
        </styleSheet>
        """;
}
