using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using Ubp.Core.Security;

namespace Ubp.Web.Services;

public sealed partial class UserManualGenerator
{
    private sealed record RazorButton(string Label, string Permission, string Handler, string Href);

    private static readonly Regex PageRouteRegex = new("@page\\s+\"([^\"]+)\"", RegexOptions.Compiled);
    private static readonly Regex PurposeRegex = new(
        "Subtitle=\"@T\\.T\\(\"([^\"]+)\"\\)\"", RegexOptions.Compiled);
    private static readonly Regex ColumnRegex = new(@"<MudTh[^>]*>(.*?)</MudTh>",
        RegexOptions.Singleline | RegexOptions.Compiled);
    private static readonly Regex TagAttributeRegex = new(
        "([A-Za-z_@][\\w:-]*)\\s*=\\s*\"([^\"]*)\"", RegexOptions.Compiled);
    private static readonly Regex PermissionRegex = new(@"Can\(ActionCodes\.(\w+)\)",
        RegexOptions.Compiled);

    private static readonly IReadOnlyDictionary<string, string> GroupPurpose =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [AppRegistry.GroupMain] = "لوحة التحكم والإشعارات: قراءة مؤشرات الأداء والاختصارات.",
            [AppRegistry.GroupDefinitions] = "التعريفات الأساسية: الفروع، المخازن، العملات، الضرائب، الفترات.",
            [AppRegistry.GroupSecurity] = "المستخدمون والأدوار وسجل الأنشطة.",
            [AppRegistry.GroupInventory] = "الأصناف والمخزون والمستندات والجرد.",
            [AppRegistry.GroupPartners] = "العملاء والموردون وتصنيفاتهم.",
            [AppRegistry.GroupAccounting] = "المحاسبة والخزينة والأصول والمشتريات.",
            [AppRegistry.GroupSales] = "الحجوزات والمواعيد والتنفيذ والفواتير وPOS.",
            [AppRegistry.GroupReports] = "التقارير القياسية والمحاسبية والمخزنية.",
            [AppRegistry.GroupSystem] = "التشغيل والصحة وقاعدة البيانات ودليل المستخدم."
        };

    private List<ManualScreenDoc> BuildScreens(ManualSourceSet source, string language)
    {
        var pageRoutes = new Dictionary<string, ManualSourceFile>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in source.Pages)
            foreach (Match match in PageRouteRegex.Matches(file.Text))
                pageRoutes[NormalizeRoute(match.Groups[1].Value)] = file;

        var screens = new List<ManualScreenDoc>();
        foreach (var screen in AppRegistry.Screens)
        {
            var file = FindSource(screen.Route, pageRoutes);
            var purpose = file is null ? "" : FindPurpose(file.Text, language);
            var columns = file is null ? [] : FindColumns(file.Text, language);
            var buttons = file is null ? [] : ParseButtons(file.Text, language, screen.Code);
            if (buttons.Count == 0) buttons = FallbackButtons(screen, language);

            screens.Add(new ManualScreenDoc(screen.Code, screen.Route,
                _translator.Get(language, screen.TitleKey),
                screen.GroupKey, _translator.Get(language, screen.GroupKey),
                purpose, columns, buttons,
                screen.Actions.Select(a => a.Code).ToList(), file is not null));
        }
        return screens;
    }

    private static string NormalizeRoute(string route) =>
        Regex.Replace(route.Trim().ToLowerInvariant(), @"\{[^}]+\}", "{x}").TrimEnd('/');

    private static ManualSourceFile? FindSource(string route,
        IReadOnlyDictionary<string, ManualSourceFile> pages)
    {
        var normalized = NormalizeRoute(route);
        if (pages.TryGetValue(normalized, out var exact)) return exact;
        var prefix = normalized.Split('{')[0];
        return pages.FirstOrDefault(pair => pair.Key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)).Value;
    }

    private string FindPurpose(string text, string language)
    {
        var match = PurposeRegex.Match(text);
        return match.Success ? _translator.Get(language, match.Groups[1].Value) : "";
    }

    private List<string> FindColumns(string text, string language)
    {
        var columns = new List<string>();
        foreach (Match match in ColumnRegex.Matches(text))
        {
            var value = ResolveText(match.Groups[1].Value, language);
            if (!string.IsNullOrWhiteSpace(value) && !columns.Contains(value)) columns.Add(value);
            if (columns.Count >= 12) break;
        }
        return columns;
    }

    private string ResolveText(string text, string language)
    {
        var clean = Regex.Replace(text, "<[^>]+>", " ");
        clean = Regex.Replace(clean, @"\s+", " ").Trim();
        var match = Regex.Match(clean, "^@T\\.T\\(\"([^\"]+)\"\\)$");
        return match.Success ? _translator.Get(language, match.Groups[1].Value) : clean;
    }

    private List<ManualButtonDoc> ParseButtons(string text, string language, string screenCode)
    {
        var lines = text.Replace("\r\n", "\n").Split('\n');
        var buttons = new List<ManualButtonDoc>();
        foreach (var tag in ExtractTags(text, "MudButton", "MudIconButton", "MudMenuItem"))
        {
            var attrs = TagAttributeRegex.Matches(tag.Tag)
                .Cast<Match>()
                .ToDictionary(m => m.Groups[1].Value, m => m.Groups[2].Value,
                    StringComparer.OrdinalIgnoreCase);
            var label = ResolveText(tag.Inner, language);
            if (string.IsNullOrWhiteSpace(label) && attrs.TryGetValue("Label", out var labelAttr))
                label = ResolveText(labelAttr, language);
            if (string.IsNullOrWhiteSpace(label) && attrs.TryGetValue("title", out var titleAttr))
                label = ResolveText(titleAttr, language);
            if (string.IsNullOrWhiteSpace(label) && attrs.TryGetValue("Icon", out var icon))
                label = icon.Split('.').LastOrDefault() ?? "زر";
            if (string.IsNullOrWhiteSpace(label)) label = "زر";

            var actionCode = FindPermission(lines, tag.Line, tag.Tag);
            var permission = string.IsNullOrWhiteSpace(actionCode)
                ? "—"
                : (string.IsNullOrWhiteSpace(screenCode) ? actionCode : $"{screenCode}/{actionCode}");
            attrs.TryGetValue("OnClick", out var onClick);
            attrs.TryGetValue("Href", out var href);
            onClick ??= ""; href ??= "";
            var behavior = BehaviorFor(actionCode, onClick, href, label);
            var destination = DestinationFor(onClick, href);
            if (buttons.Any(b => b.Label == label && b.Permission == permission &&
                                 b.Handler == onClick && b.Destination == destination)) continue;
            buttons.Add(new ManualButtonDoc(label, permission, onClick, behavior, destination));
            if (buttons.Count >= 40) break;
        }
        return buttons;
    }

    private static string FindPermission(IReadOnlyList<string> lines, int line, string tag)
    {
        var tagMatches = PermissionRegex.Matches(tag);
        if (tagMatches.Count > 0) return ResolveAction(tagMatches[^1].Groups[1].Value);

        var start = Math.Max(0, line - 15);
        for (var i = line - 2; i >= start; i--)
        {
            var text = lines[i];
            if (text.Contains("</MudButton>", StringComparison.Ordinal) ||
                text.Contains("</MudIconButton>", StringComparison.Ordinal) ||
                text.Contains("</MudMenuItem>", StringComparison.Ordinal)) break;
            var matches = PermissionRegex.Matches(text);
            if (matches.Count > 0) return ResolveAction(matches[^1].Groups[1].Value);
        }
        return "";
    }

    private static string ResolveAction(string name) =>
        typeof(ActionCodes).GetField(name, BindingFlags.Public | BindingFlags.Static)
            ?.GetValue(null) as string ?? name;

    private static string BehaviorFor(string actionCode, string onClick, string href, string label)
    {
        if (!string.IsNullOrWhiteSpace(href)) return $"ينتقل إلى `{href}`.";

        var handler = onClick.ToLowerInvariant();
        if (handler == "cancel" || handler.Contains("mudialog.cancel"))
            return "يغلق الحوار دون حفظ.";
        if (handler.Contains("cancelasync"))
            return "يلغي السجل بعد التأكيد. **القيود:** حسب الحالة؛ قد يتطلب عكسًا إذا كان مرحّلًا.";
        if (handler == "close" || handler.Contains("mudialog.close"))
            return "يغلق الحوار.";
        if (handler.Contains("confirm"))
            return "ينفّذ العملية ويغلق الحوار.";
        if (handler.Contains("reload") || handler.Contains("refresh") || handler.Contains("load") ||
            handler.Contains("search")) return "يعيد تحميل/يفلتر البيانات.";
        if (handler.Contains("print")) return "يفتح نافذة الطباعة.";
        if (handler.Contains("export")) return "ينزّل ملفًا.";
        if (handler.Contains("addline") || handler.Contains("removeline") ||
            handler.Contains("addrow") || handler.Contains("remove")) return "يعدّل البنود في الذاكرة قبل الحفظ.";
        if (handler.Contains("save"))
            return "يحفظ النموذج بعد التحقق من الحقول. **القيود:** تعارض النسخة يمنع الحفظ إذا عُدّل السجل من مستخدم آخر.";
        if (handler.Contains("cancel"))
            return "يلغي السجل بعد التأكيد. **القيود:** حسب الحالة؛ قد يتطلب عكسًا إذا كان مرحّلًا.";
        if (!string.IsNullOrWhiteSpace(actionCode) && Guide.TryGetValue(actionCode, out var guide))
            return $"{guide.Does} **القيود:** {guide.Limits}";
        if (handler.Contains("delete")) return "يحذف/يعطّل السجل بعد التأكيد.";
        if (handler.Contains("post")) return "يرحّل المستند/القيد.";
        if (handler.Contains("reverse")) return "يعكس الأثر.";
        return label.Contains("إضافة") || label.Contains("تعديل") ? "يفتح حوارًا ويحفظ بعد التحقق." : "ينفّذ إجراءً على الشاشة.";
    }

    private static string DestinationFor(string onClick, string href)
    {
        if (!string.IsNullOrWhiteSpace(href)) return href;
        var handler = onClick.ToLowerInvariant();
        if (handler.Contains("reload") || handler.Contains("refresh") || handler.Contains("load") ||
            handler.Contains("search")) return "يبقى في الصفحة";
        if (handler.Contains("print") || handler.Contains("export")) return "لا ينتقل";
        return "يبقى في الصفحة (حوار/تحديث)";
    }

    private List<ManualButtonDoc> FallbackButtons(ScreenDefinition screen, string language) =>
        screen.Actions.Select(action => new ManualButtonDoc(
            ActionTitle(action.Code, language),
            $"{screen.Code}/{action.Code}", "",
            Guide.TryGetValue(action.Code, out var guide) ? $"{guide.Does} **القيود:** {guide.Limits}" : "إجراء مسجل.",
            "حسب الشاشة")).ToList();

    private void AppendScreenSections(StringBuilder md, IReadOnlyList<ManualScreenDoc> screens,
        string language)
    {
        md.AppendLine("## 4. دليل الشاشات والأزرار");
        md.AppendLine();
        md.AppendLine("الأزرار مستخرجة من ملفات `.razor` الحالية. لكل زر: الصلاحية، القيود/السلوك، والانتقال. " +
                      "الأزرار التي لا يوجد لها مصدر تُعرض من أفعال `AppRegistry`.");
        md.AppendLine();
        foreach (var group in screens.GroupBy(s => (s.GroupKey, s.GroupTitle)))
        {
            md.AppendLine($"### {group.Key.GroupTitle}");
            md.AppendLine();
            if (GroupPurpose.TryGetValue(group.Key.GroupKey, out var purpose))
            {
                md.AppendLine(purpose);
                md.AppendLine();
            }
            foreach (var screen in group)
            {
                md.AppendLine($"#### `{screen.Route}` — {screen.Title}");
                md.AppendLine();
                md.AppendLine(screen.Purpose.Length > 0 ? screen.Purpose : $"شاشة {screen.Title}.");
                md.AppendLine();
                if (screen.Columns.Count > 0)
                    md.AppendLine($"**الأعمدة:** {string.Join(" · ", screen.Columns)}");
                md.AppendLine();
                md.AppendLine("| الزر | الصلاحية | ماذا يفعل / القيود | إلى أين |");
                md.AppendLine("|---|---|---|---|");
                foreach (var button in screen.Buttons)
                    md.AppendLine($"| {button.Label} | `{button.Permission}` | {button.Behavior} | {button.Destination} |");
                if (screen.Buttons.Count == 0) md.AppendLine("| — | — | لا توجد أزرار تنفيذية. | — |");
                md.AppendLine();
                if (!screen.SourceAvailable)
                    md.AppendLine("> ملف المصدر غير متاح لهذه الشاشة في نسخة النشر؛ الأزرار معروضة من `AppRegistry`.");
                md.AppendLine();
            }
        }
    }

    private void AppendPermissionMatrix(StringBuilder md, IReadOnlyList<ManualScreenDoc> screens,
        string language)
    {
        md.AppendLine("## 7. مصفوفة الصلاحيات");
        md.AppendLine();
        md.AppendLine("| الشاشة | المسار | الأكواد المتاحة |");
        md.AppendLine("|---|---|---|");
        foreach (var screen in screens)
        {
            var actions = string.Join(", ", screen.Actions.Select(a => $"`{screen.Code}/{a}`"));
            md.AppendLine($"| {screen.Title} | `{screen.Route}` | {actions} |");
        }
        md.AppendLine();
        md.AppendLine("> المدير `IsAdmin` يتجاوز كل هذه الأكواد؛ Super Admin يضيف إدارة أعلام المديرين وقاعدة البيانات.");
        md.AppendLine();
    }

    private sealed record RazorTag(string Tag, string Inner, int Line);

    private static IEnumerable<RazorTag> ExtractTags(string text, params string[] names)
    {
        foreach (var name in names)
        {
            var search = "<" + name;
            var index = 0;
            while ((index = text.IndexOf(search, index, StringComparison.Ordinal)) >= 0)
            {
                var start = index;
                var i = index + search.Length;
                if (i < text.Length && !char.IsWhiteSpace(text[i]) && text[i] != '>' && text[i] != '/')
                {
                    index += search.Length;
                    continue;
                }
                var j = i;
                char? quote = null;
                while (j < text.Length)
                {
                    var c = text[j];
                    if (quote is not null) { if (c == quote) quote = null; }
                    else if (c is '"' or '\'') quote = c;
                    else if (c == '>') break;
                    j++;
                }
                if (j >= text.Length) break;
                var tag = text[start..(j + 1)];
                var close = text.IndexOf("</" + name + ">", j, StringComparison.Ordinal);
                var inner = close >= 0 ? text[(j + 1)..close] : "";
                var line = text[..start].Count(c => c == '\n') + 1;
                yield return new RazorTag(tag, inner, line);
                index = close >= 0 ? close + name.Length + 3 : j + 1;
            }
        }
    }
}
