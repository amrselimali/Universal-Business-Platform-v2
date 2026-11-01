using System.Collections.Concurrent;
using System.Text;
using Ubp.Application.Services;
using Ubp.Core.Security;

namespace Ubp.Web.Services;

/// <summary>
/// يولّد دليل المستخدم من الكود المصدري وقاعدة البيانات في وقت التشغيل:
/// الشاشات/الصلاحيات من AppRegistry، الأزرار من ملفات Razor، الجداول من PostgreSQL،
/// مع دورات العمل وقيود الأزرار والشرح السردي.
/// </summary>
public sealed partial class UserManualGenerator
{
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static readonly ConcurrentDictionary<string, UserManualGeneration> Cache =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly UserManualSourceReader _sources;
    private readonly IManualSchemaProvider _schema;
    private readonly Translator _translator;
    private readonly ILogger<UserManualGenerator> _logger;

    public UserManualGenerator(UserManualSourceReader sources, IManualSchemaProvider schema,
        Translator translator, ILogger<UserManualGenerator> logger)
    {
        _sources = sources;
        _schema = schema;
        _translator = translator;
        _logger = logger;
    }

    public async Task<UserManualGeneration> GenerateAsync(ActorContext actor, bool force,
        CancellationToken ct = default)
    {
        var language = actor.Language == "en" ? "en" : "ar";
        if (!force && Cache.TryGetValue(language, out var cached)) return cached;

        await Gate.WaitAsync(ct);
        try
        {
            if (!force && Cache.TryGetValue(language, out cached)) return cached;

            var source = _sources.Read();
            var screens = BuildScreens(source, language);
            var dialogs = BuildDialogs(source, language);
            var tables = await LoadTablesAsync(actor, ct);

            var md = new StringBuilder(96 * 1024);
            AppendHeader(md, screens, dialogs, tables, source);
            AppendHowItWorks(md);
            AppendSystemMap(md, screens, language);
            AppendWorkflowSections(md, language);
            AppendScreenSections(md, screens, language);
            AppendDialogSections(md, dialogs);
            AppendDatabaseSection(md, tables, language);
            AppendActionGuide(md, language);
            AppendPermissionMatrix(md, screens, language);
            AppendAuditAppendix(md, source, language);
            AppendNotes(md);

            var result = new UserManualGeneration(md.ToString(), DateTimeOffset.Now,
                screens.Count, screens.Sum(screen => screen.Buttons.Count) +
                              dialogs.Sum(dialog => dialog.Buttons.Count),
                tables.Count, tables.Count > 0, source.HasAny);
            Cache[language] = result;
            return result;
        }
        finally
        {
            Gate.Release();
        }
    }

    private void AppendHeader(StringBuilder md, IReadOnlyList<ManualScreenDoc> screens,
        IReadOnlyList<ManualDialogDoc> dialogs, IReadOnlyList<ManualTableDoc> tables,
        ManualSourceSet source)
    {
        md.AppendLine("# دليل المستخدم الشامل — Universal Business Platform");
        md.AppendLine();
        md.AppendLine($"> تم توليد هذا الدليل تلقائيًا من **الكود المصدري وقاعدة البيانات** في " +
                      $"{DateTimeOffset.Now:yyyy-MM-dd HH:mm}.");
        md.AppendLine($"> الشاشات: **{screens.Count}** · الأزرار: **{screens.Sum(s => s.Buttons.Count) + dialogs.Sum(d => d.Buttons.Count)}** " +
                      $"· الحوارات: **{dialogs.Count}** · الجداول: **{tables.Count}** · ملفات المصدر: **{source.Pages.Count + source.Components.Count + source.Services.Count}**.");
        md.AppendLine("> اضغط **تحديث الدليل** لإعادة التوليد من الكود والجداول الحالية.");
        md.AppendLine();
    }

    private static void AppendHowItWorks(StringBuilder md)
    {
        md.AppendLine("## 1. كيف يعمل النظام");
        md.AppendLine();
        md.AppendLine("النظام مبني على **Default Deny**: أي شاشة أو زر غير ممنوح للدور لا يظهر ولا يعمل. " +
                      "الصلاحية الفعلية تُحسب من أدوار المستخدم أولاً، ثم استثناءات `user_permissions` الفردية تتغلب عليها.");
        md.AppendLine();
        md.AppendLine("- **المدير `IsAdmin`** يتجاوز كل فحوص الصلاحيات، لكنه لا يرى حسابات Super Admin إلا إذا كان Super Admin.");
        md.AppendLine("- **Super Admin** يضيف تجاوز نطاق الفروع، وإدارة أعلام المديرين، وشاشة قاعدة البيانات.");
        md.AppendLine("- **نطاق الصلاحية**: `0 = سجلاتي`، `1 = فروعي`، `2 = الكل`.");
        md.AppendLine("- **دورة المستند**: مسودة ← ترحيل ← (عكس فقط). المرحّل لا يُعدّل؛ التصحيح بعكس القيد/المستند.");
        md.AppendLine("- **الحذف**: يتحول إلى تعطيل (`UPDATE`) إذا كان السجل مستخدمًا وله تاريخ؛ لا يُحذف تاريخه.");
        md.AppendLine("- **السجلات**: كل عملية تُكتب في `audit_log` من الخدمات (الجدول + الإجراء + old/new data + المستخدم/الفرع/Trace).");
        md.AppendLine("- **الفروع**: الاستعلامات تُقص على الفروع المسموحة على السيرفر، وليس في الواجهة فقط.");
        md.AppendLine();
    }

    private static void AppendSystemMap(StringBuilder md, IReadOnlyList<ManualScreenDoc> screens,
        string language)
    {
        md.AppendLine("## 2. خريطة النظام");
        md.AppendLine();
        md.AppendLine("| المجموعة | الشاشات | أمثلة المسارات |");
        md.AppendLine("|---|---|---|");
        foreach (var group in screens.GroupBy(s => (s.GroupKey, s.GroupTitle)))
        {
            var routes = string.Join(", ", group.Take(4).Select(s => $"`{s.Route}`"));
            md.AppendLine($"| {group.Key.GroupTitle} | {group.Count()} | {routes} |");
        }
        md.AppendLine();
    }

    private static void AppendNotes(StringBuilder md)
    {
        md.AppendLine("## 9. ملاحظات مهمة");
        md.AppendLine();
        md.AppendLine("- هذا الدليل **مولّد آليًا**؛ لا تعدّله يدويًا. عدّل الكود/الملف المصدر ثم اضغط **تحديث الدليل**.");
        md.AppendLine("- الشاشات والصلاحيات تأتي من `AppRegistry`، والأزرار من ملفات `.razor`، والجداول من `information_schema`.");
        md.AppendLine("- زر التحديث يقرأ الملفات الحالية؛ لتطبيق تغيير سلوكي فعليًا أعد بناء/تشغيل التطبيق، لأن الكود المُنفَّذ هو النسخة المبنية.");
        md.AppendLine("- إذا كان مصدر الأزرار غير متاح في نسخة النشر، تُعرض الأزرار من أفعال `AppRegistry` كبديل.");
        md.AppendLine("- الأزرار التي لم يُعثر لها على صلاحية صريحة تظهر بصلاحية `—`؛ راجع `AppRegistry` أو أضف `Can(ActionCodes.X)` في الواجهة.");
        md.AppendLine("- دورات العمل وقيود الأزرار تأتي من كتالوج مركزي مطابق لقواعد الخدمات في `UserManualGenerator.Workflows.cs`؛ عند تغيير منطق العمل حدّثه مع الكود.");
        md.AppendLine("- جدول «أين تكتب الخدمات في audit_log» يُستخرج من ملفات الخدمات المصدرية، لذا يعكس تغييرات `_audit.WriteAsync` تلقائيًا.");
    }
}
