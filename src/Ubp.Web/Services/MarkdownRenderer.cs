using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace Ubp.Web.Services;

/// <summary>
/// محول Markdown مبسّط لدليل المستخدم: عناوين، جداول، قوائم، اقتباسات، أكواد، وروابط.
/// لا يعتمد على أي حزمة خارجية، ويُهرّب HTML قبل العرض لمنع الحقن.
/// </summary>
public static class MarkdownRenderer
{
    private static readonly Regex Heading = new(@"^(#{1,6})\s+(.+)$", RegexOptions.Compiled);
    private static readonly Regex Bullet = new(@"^\s*[-*+]\s+(.*)$", RegexOptions.Compiled);
    private static readonly Regex Ordered = new(@"^\s*\d+[.)]\s+(.*)$", RegexOptions.Compiled);
    private static readonly Regex TableSeparator = new(
        @"^\|?\s*:?-{2,}:?\s*(\|\s*:?-{2,}:?\s*)+\|?$", RegexOptions.Compiled);
    private static readonly Regex CodeSpan = new("`([^`]+)`", RegexOptions.Compiled);
    private static readonly Regex Bold = new(@"\*\*([^*]+)\*\*", RegexOptions.Compiled);
    private static readonly Regex Link = new(@"\[([^\]]+)\]\(([^)]+)\)", RegexOptions.Compiled);
    private static readonly Regex HorizontalRule = new(
        @"^(\*\s*){3,}$|^(-\s*){3,}$|^(_\s*){3,}$", RegexOptions.Compiled);

    public static string Render(string markdown)
    {
        if (string.IsNullOrWhiteSpace(markdown)) return string.Empty;

        var lines = markdown.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        var html = new StringBuilder(markdown.Length * 2);
        var i = 0;

        while (i < lines.Length)
        {
            var line = lines[i];
            if (string.IsNullOrWhiteSpace(line)) { i++; continue; }

            if (line.TrimStart().StartsWith("```", StringComparison.Ordinal))
            {
                var language = line.Trim()[3..].Trim();
                i++;
                var code = new StringBuilder();
                while (i < lines.Length &&
                       !lines[i].TrimStart().StartsWith("```", StringComparison.Ordinal))
                {
                    code.AppendLine(lines[i]);
                    i++;
                }
                if (i < lines.Length) i++;
                if (string.Equals(language, "flow", StringComparison.OrdinalIgnoreCase))
                {
                    AppendFlow(html, code.ToString());
                    continue;
                }
                html.Append("<pre><code");
                if (!string.IsNullOrWhiteSpace(language))
                    html.Append(" class=\"language-").Append(WebUtility.HtmlEncode(language)).Append('"');
                html.Append('>').Append(WebUtility.HtmlEncode(code.ToString())).Append("</code></pre>\n");
                continue;
            }

            if (HorizontalRule.IsMatch(line.Trim()))
            {
                html.Append("<hr />\n");
                i++;
                continue;
            }

            var heading = Heading.Match(line);
            if (heading.Success)
            {
                var level = heading.Groups[1].Value.Length;
                html.Append("<h").Append(level).Append('>')
                    .Append(Inline(heading.Groups[2].Value.Trim()))
                    .Append("</h").Append(level).Append(">\n");
                i++;
                continue;
            }

            if (line.TrimStart().StartsWith('>'))
            {
                var quote = new StringBuilder();
                while (i < lines.Length && lines[i].TrimStart().StartsWith('>'))
                {
                    var content = lines[i].TrimStart()[1..].TrimStart();
                    if (quote.Length > 0) quote.Append(' ');
                    quote.Append(content);
                    i++;
                }
                html.Append("<blockquote>").Append(Inline(quote.ToString())).Append("</blockquote>\n");
                continue;
            }

            if (line.TrimStart().StartsWith('|'))
            {
                var table = new List<string>();
                while (i < lines.Length && lines[i].TrimStart().StartsWith('|'))
                {
                    table.Add(lines[i].Trim());
                    i++;
                }
                AppendTable(html, table);
                continue;
            }

            if (Bullet.IsMatch(line))
            {
                html.Append("<ul>\n");
                while (i < lines.Length && Bullet.IsMatch(lines[i]))
                {
                    html.Append("<li>")
                        .Append(Inline(Bullet.Match(lines[i]).Groups[1].Value.Trim()))
                        .Append("</li>\n");
                    i++;
                }
                html.Append("</ul>\n");
                continue;
            }

            if (Ordered.IsMatch(line))
            {
                html.Append("<ol>\n");
                while (i < lines.Length && Ordered.IsMatch(lines[i]))
                {
                    html.Append("<li>")
                        .Append(Inline(Ordered.Match(lines[i]).Groups[1].Value.Trim()))
                        .Append("</li>\n");
                    i++;
                }
                html.Append("</ol>\n");
                continue;
            }

            var paragraph = new StringBuilder();
            while (i < lines.Length && !string.IsNullOrWhiteSpace(lines[i]) && !IsBlockStart(lines[i]))
            {
                if (paragraph.Length > 0) paragraph.Append(' ');
                paragraph.Append(lines[i].Trim());
                i++;
            }
            html.Append("<p>").Append(Inline(paragraph.ToString())).Append("</p>\n");
        }

        return html.ToString();
    }

    private static void AppendFlow(StringBuilder html, string text)
    {
        var steps = text.Replace("\r\n", "\n")
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (steps.Length == 0) return;

        html.Append("<div class=\"ubp-flow\" dir=\"rtl\">");
        for (var i = 0; i < steps.Length; i++)
        {
            if (i > 0) html.Append("<span class=\"ubp-flow-arrow\" aria-hidden=\"true\">←</span>");
            html.Append("<div class=\"ubp-flow-step\"><span class=\"ubp-flow-num\">")
                .Append(i + 1)
                .Append("</span><span>")
                .Append(Inline(steps[i]))
                .Append("</span></div>");
        }
        html.Append("</div>\n");
    }

    private static void AppendTable(StringBuilder html, IReadOnlyList<string> rows)
    {
        var cells = rows.Select(SplitRow).ToList();
        var hasHeader = cells.Count > 1 && TableSeparator.IsMatch(rows[1]);
        var bodyStart = hasHeader ? 2 : 0;

        html.Append("<div class=\"ubp-manual-table-wrap\"><table class=\"ubp-manual-table\">\n");
        if (hasHeader)
        {
            html.Append("<thead><tr>");
            foreach (var cell in cells[0])
                html.Append("<th>").Append(Inline(cell)).Append("</th>");
            html.Append("</tr></thead>\n");
        }
        html.Append("<tbody>\n");
        for (var r = bodyStart; r < cells.Count; r++)
        {
            if (cells[r].Count == 0) continue;
            html.Append("<tr>");
            foreach (var cell in cells[r])
                html.Append("<td>").Append(Inline(cell)).Append("</td>");
            html.Append("</tr>\n");
        }
        html.Append("</tbody></table></div>\n");
    }

    private static IReadOnlyList<string> SplitRow(string row)
    {
        var trimmed = row.Trim().Trim('|');
        return trimmed.Split('|').Select(c => c.Trim()).ToList();
    }

    private static bool IsBlockStart(string line)
    {
        var trimmed = line.TrimStart();
        return Heading.IsMatch(line) || trimmed.StartsWith('>') || trimmed.StartsWith('|') ||
               trimmed.StartsWith("```", StringComparison.Ordinal) ||
               HorizontalRule.IsMatch(line.Trim()) || Bullet.IsMatch(line) || Ordered.IsMatch(line);
    }

    private static string Inline(string text)
    {
        var encoded = WebUtility.HtmlEncode(text);
        encoded = CodeSpan.Replace(encoded, m => $"<code>{m.Groups[1].Value}</code>");
        encoded = Bold.Replace(encoded, "<strong>$1</strong>");
        encoded = Link.Replace(encoded, m =>
        {
            var href = m.Groups[2].Value;
            return href.StartsWith("javascript:", StringComparison.OrdinalIgnoreCase) ||
                   href.StartsWith("data:", StringComparison.OrdinalIgnoreCase)
                ? m.Value
                : $"<a href=\"{href}\">{m.Groups[1].Value}</a>";
        });
        return encoded;
    }
}
