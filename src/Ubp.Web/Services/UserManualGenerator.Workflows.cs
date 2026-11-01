using System.Text;
using System.Text.RegularExpressions;
using Ubp.Core.Security;

namespace Ubp.Web.Services;

public sealed partial class UserManualGenerator
{
    private sealed record WorkflowStep(string Label, string Route, string Action);
    private sealed record Workflow(string Title, string Purpose, IReadOnlyList<WorkflowStep> Steps);

    private static readonly IReadOnlyList<Workflow> Workflows =
    [
        new("دورة المشتريات", "من إنشاء فاتورة الشراء حتى سداد المورد وتخصيص الدفعة.",
        [
            new("إنشاء فاتورة شراء", "/purchasing/invoices", ActionCodes.Create),
            new("ترحيل الفاتورة", "/purchasing/invoices", ActionCodes.Post),
            new("إنشاء سند دفع", "/treasury/vouchers", ActionCodes.Create),
            new("ترحيل سند الدفع", "/treasury/vouchers", ActionCodes.Post),
            new("تخصيص الدفعة", "/treasury/vouchers", ActionCodes.Edit)
        ]),
        new("دورة المبيعات", "من الحجز حتى الفاتورة والتحصيل/التسوية.",
        [
            new("إنشاء حجز", "/sales/bookings", ActionCodes.Create),
            new("تأكيد الحجز", "/sales/bookings", ActionCodes.Confirm),
            new("إنشاء إذن تنفيذ", "/sales/deliveries", ActionCodes.Execute),
            new("ترحيل إذن التنفيذ", "/sales/deliveries", ActionCodes.Post),
            new("إنشاء فاتورة مبيعات", "/sales/invoices", ActionCodes.Create),
            new("ترحيل الفاتورة", "/sales/invoices", ActionCodes.Post),
            new("تسوية الحجز", "/sales/settlements", ActionCodes.Create),
            new("اعتماد التسوية", "/sales/settlements", ActionCodes.Approve),
            new("ترحيل التسوية", "/sales/settlements", ActionCodes.Post)
        ]),
        new("دورة المخزون", "استلام/صرف/تحويل ثم ترحيل، والتصحيح بالعكس.",
        [
            new("إنشاء مستند مخزون", "/inventory/documents", ActionCodes.Add),
            new("ترحيل المستند", "/inventory/documents", ActionCodes.Post),
            new("استلام التحويل", "/inventory/documents", ActionCodes.Receive),
            new("عكس مستند مرحّل", "/inventory/documents", ActionCodes.Reverse)
        ]),
        new("دورة الجرد", "إنشاء جلسة جرد، إدخال الكميات، ثم ترحيل الفروق.",
        [
            new("إنشاء جرد", "/inventory/stocktake", ActionCodes.Add),
            new("إدخال الكميات", "/inventory/stocktake", ActionCodes.Edit),
            new("ترحيل الجرد", "/inventory/stocktake", ActionCodes.Post)
        ]),
        new("دورة المحاسبة", "قيد مسودة، ترحيل، عكس، ثم إغلاق الفترة.",
        [
            new("إنشاء قيد", "/accounting/entries", ActionCodes.Create),
            new("حفظ مسودة", "/accounting/entries/new", ActionCodes.SaveDraft),
            new("ترحيل القيد", "/accounting/entries/new", ActionCodes.Post),
            new("عكس القيد", "/accounting/entries", ActionCodes.Reverse),
            new("إغلاق الفترة", "/accounting/periods", ActionCodes.ClosePeriod)
        ]),
        new("دورة الخزينة", "سند قبض/صرف، ترحيل، تخصيص، ثم وردية وفتح/إغلاق.",
        [
            new("إنشاء سند", "/treasury/vouchers", ActionCodes.Create),
            new("ترحيل السند", "/treasury/vouchers", ActionCodes.Post),
            new("تخصيص على مستند", "/treasury/vouchers", ActionCodes.Edit),
            new("فتح وردية", "/treasury/shifts", ActionCodes.Open),
            new("إغلاق الوردية", "/treasury/shifts", ActionCodes.Close)
        ]),
        new("دورة الأصول الثابتة", "شراء الأصل، قراءة العدّاد، الصيانة، الإهلاك، ثم الاستبعاد.",
        [
            new("إضافة أصل", "/assets/management", ActionCodes.Add),
            new("قراءة عدّاد", "/assets/management", ActionCodes.AddMeterReading),
            new("فتح أمر صيانة", "/assets/maintenance", ActionCodes.OpenMaintenance),
            new("إغلاق أمر صيانة", "/assets/maintenance", ActionCodes.CloseMaintenance),
            new("تشغيل الإهلاك", "/assets/depreciation", ActionCodes.RunDepreciation),
            new("استبعاد الأصل", "/assets/management", ActionCodes.Edit)
        ]),
        new("دورة الصلاحيات", "دور ← صلاحيات ← مستخدم ← فحص الوصول.",
        [
            new("إنشاء دور", "/roles", ActionCodes.Add),
            new("ضبط الصلاحيات", "/roles", ActionCodes.Edit),
            new("إضافة مستخدم", "/users", ActionCodes.Add),
            new("مراجعة الصلاحيات", "/users", ActionCodes.Edit)
        ])
    ];

    private void AppendWorkflowSections(StringBuilder md, string language)
    {
        md.AppendLine("## 3. دورات العمل");
        md.AppendLine();
        md.AppendLine("كل دورة موضّحة كمخطط تسلسلي ثم جدول بالخطوات والصلاحيات والنتيجة. " +
                      "الخطوات المرتبطة بشاشات غير موجودة في هذه النسخة تُستبعد تلقائيًا.");
        md.AppendLine();
        foreach (var workflow in Workflows)
        {
            var steps = workflow.Steps.Where(step => FindScreen(step.Route) is not null &&
                FindScreen(step.Route)!.Actions.Any(a =>
                    string.Equals(a.Code, step.Action, StringComparison.OrdinalIgnoreCase))).ToList();
            if (steps.Count == 0) continue;
            md.AppendLine($"### {workflow.Title}");
            md.AppendLine();
            md.AppendLine(workflow.Purpose);
            md.AppendLine();
            md.AppendLine("```flow");
            foreach (var step in steps) md.AppendLine(step.Label);
            md.AppendLine("```");
            md.AppendLine();
            md.AppendLine("| # | الخطوة | الشاشة | الصلاحية | النتيجة |");
            md.AppendLine("|---|---|---|---|---|");
            for (var i = 0; i < steps.Count; i++)
            {
                var step = steps[i];
                var screen = FindScreen(step.Route)!;
                md.AppendLine($"| {i + 1} | {step.Label} | `{step.Route}` | " +
                              $"`{screen.Code}/{step.Action}` | {ActionTitle(step.Action, language)} |");
            }
            md.AppendLine();
        }
    }

    private static ScreenDefinition? FindScreen(string route)
    {
        var exact = AppRegistry.Screens.FirstOrDefault(screen => screen.Route == route);
        if (exact is not null) return exact;
        var normalized = NormalizeRoute(route);
        var prefix = normalized.Contains('{')
            ? normalized[..normalized.IndexOf('{')]
            : normalized[..(normalized.LastIndexOf('/') + 1)];
        if (string.IsNullOrEmpty(prefix)) return null;
        return AppRegistry.Screens.FirstOrDefault(screen =>
        {
            var candidate = NormalizeRoute(screen.Route);
            return candidate.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) &&
                   (normalized.Contains('{') || candidate.Contains("{x}"));
        });
    }

    private string ActionTitle(string action, string language)
    {
        var key = $"action.{action.ToLowerInvariant()}";
        var text = _translator.Get(language, key);
        if (!string.Equals(text, key, StringComparison.Ordinal)) return text;
        return Guide.TryGetValue(action, out var guide) ? guide.Name : action;
    }

    private static readonly IReadOnlyDictionary<string, (string Name, string Does, string Limits, string Audit)> Guide =
        new Dictionary<string, (string, string, string, string)>(StringComparer.OrdinalIgnoreCase)
        {
            [ActionCodes.View] = ("عرض", "قراءة فقط حسب نطاق الفروع.", "لا يعدّل شيئًا.", "لا شيء"),
            [ActionCodes.Add] = ("إضافة", "يفتح حوار إنشاء سجل جديد.", "حقول إلزامية + تحقق من التكرار/المرجع.", "INSERT"),
            [ActionCodes.Create] = ("إنشاء", "ينشئ سجلًا جديدًا.", "حقول إلزامية + تحقق من التكرار.", "INSERT"),
            [ActionCodes.Edit] = ("تعديل", "يفتح حوار تعديل السجل.", "المسودات فقط غالبًا؛ تعارض النسخة يمنع الحفظ.", "UPDATE"),
            [ActionCodes.Delete] = ("حذف", "حذف نهائي إن لم يُستخدم.", "إن كان مستخدمًا/له تاريخ يتحول إلى تعطيل.", "DELETE/UPDATE"),
            [ActionCodes.ToggleActive] = ("تفعيل/تعطيل", "يغيّر حالة السجل دون حذفه.", "تعطيل المستخدم يلغي جلساته؛ آخر Super Admin محمي.", "UPDATE"),
            [ActionCodes.Post] = ("ترحيل", "يرحّل المستند/القيد إلى الأستاذ/المخزون.", "مسودة فقط، فترة مفتوحة، توازن/كميات كافية، ثم لا يُعدّل.", "UPDATE + قيود"),
            [ActionCodes.Reverse] = ("عكس", "ينشئ مستند/قيد عكس للأثر.", "المرحّل فقط؛ الفترة مفتوحة؛ لا يحذف الأصل.", "INSERT + UPDATE"),
            [ActionCodes.Receive] = ("استلام", "يستلم تحويل مخزون.", "التحويل مرحّل وغير مستلم سابقًا.", "INSERT"),
            [ActionCodes.CancelEntry] = ("إلغاء", "يلغي مستندًا/قيدًا.", "حسب الحالة؛ قد يتطلب عكسًا إذا كان مرحّلًا.", "UPDATE"),
            [ActionCodes.Print] = ("طباعة", "يفتح نافذة الطباعة.", "قراءة فقط.", "لا شيء"),
            [ActionCodes.ExportCsv] = ("تصدير CSV", "ينزّل ملف CSV.", "قراءة فقط.", "لا شيء"),
            [ActionCodes.ExportXlsx] = ("تصدير XLSX", "ينزّل ملف Excel.", "قراءة فقط.", "لا شيء"),
            [ActionCodes.Export] = ("تصدير", "ينزّل/يصدر التقرير.", "قراءة فقط.", "لا شيء"),
            [ActionCodes.LinkBranches] = ("ربط الفروع", "يربط السجل بفروع.", "فرع واحد على الأقل.", "UPDATE"),
            [ActionCodes.ManageBranches] = ("إدارة الفروع", "يربط المستخدم بفروع.", "الفروع موجودة ونشطة.", "UPDATE"),
            [ActionCodes.ManageWarehouses] = ("إدارة المخازن", "يربط المستخدم بمخازن.", "المخازن داخل فروعه.", "UPDATE"),
            [ActionCodes.ManageRoles] = ("إدارة الأدوار", "يسند أدوارًا للمستخدم.", "الدور نشط.", "UPDATE"),
            [ActionCodes.ManagePermissions] = ("إدارة الصلاحيات", "يضبط الاستثناءات الفردية.", "تتغلب على صلاحيات الأدوار.", "PERMISSION_CHANGE"),
            [ActionCodes.AutoBalanceEquity] = ("موازنة حقوق الملكية", "يضيف سطر موازنة على حقوق الملكية.", "داخل نموذج الأرصدة الافتتاحية.", "لا شيء"),
            [ActionCodes.ImportCsv] = ("استيراد CSV", "معاينة ثم تنفيذ داخل معاملة.", "أخطاء/عدم توازن = صفر كتابة + import_jobs FAILED.", "import_jobs"),
            [ActionCodes.Preview] = ("معاينة", "عرض النتيجة دون كتابة.", "لا يعدّل البيانات.", "لا شيء"),
            [ActionCodes.GenerateSingle] = ("توليد قيد", "يولّد قيدًا للمستند المحدد.", "المستند مرحّل وبلا قيد سابق.", "INSERT"),
            [ActionCodes.GenerateAll] = ("توليد الكل", "يولّد قيودًا للمستندات المرحّلة بلا قيد.", "idempotent; لا يكرر القيود.", "INSERT"),
            [ActionCodes.ClosePeriod] = ("إغلاق الفترة", "يغلق نطاقًا محاسبيًا.", "يمنع الترحيل/العكس داخله.", "UPDATE"),
            [ActionCodes.ReopenPeriod] = ("إعادة فتح الفترة", "يعكس إغلاق النطاق.", "يتطلب صلاحية وسببًا.", "UPDATE"),
            [ActionCodes.RunMigrations] = ("تنفيذ الترحيلات", "يشغّل سكربتات SQL المعلّقة.", "Super Admin + قفل جلسي + معاملة لكل سكربت.", "MIGRATION"),
            [ActionCodes.EditRows] = ("تعديل الصفوف", "تعديل/حذف صف قاعدة بيانات مباشرة.", "Super Admin؛ أعمدة محمية؛ audit إلزامي.", "UPDATE/DELETE"),
            [ActionCodes.ResetPassword] = ("إعادة تعيين كلمة المرور", "يضبط كلمة مرور جديدة.", "سياسة كلمة المرور؛ يلغي كل الجلسات.", "PASSWORD_RESET"),
            [ActionCodes.Unlock] = ("فك القفل", "يصفّر المحاولات الفاشلة.", "لا يغيّر كلمة المرور.", "UPDATE"),
            [ActionCodes.ConvertToAdvance] = ("إلى دفعة مقدمة", "ينشئ سند قبض مرحّلًا للمتبقي.", "مرة واحدة لكل حجز؛ يتطلب خزينة/طريقة دفع.", "vouchers + قيود"),
            [ActionCodes.ReleaseReservation] = ("تحرير الحجز", "يلغي الحجز ويحرر الكميات.", "سبب مطلوب؛ لا يمس القيود المرحّلة.", "bookings:UPDATE"),
            [ActionCodes.ExtendReservation] = ("تمديد الحجز", "يمد صلاحية الحجز.", "تاريخ جديد > الحالي؛ لا يمس الكميات.", "reservation_adjustments"),
            [ActionCodes.RunDepreciation] = ("تشغيل الإهلاك", "يحسب إهلاك الشهر/السنة.", "الأصل نشط وغير مستبعد؛ يمنع التكرار لنفس الفترة.", "INSERT + قيود"),
            [ActionCodes.AddMeterReading] = ("قراءة عدّاد", "يضيف قراءة تصاعدية.", "الأصل بعدّاد وغير مستبعد؛ القراءة لا تقل عن السابقة.", "asset_meter_readings"),
            [ActionCodes.OpenMaintenance] = ("فتح صيانة", "ينشئ أمر/خطة صيانة.", "الأصل نشط؛ التكلفة اختيارية.", "INSERT"),
            [ActionCodes.CloseMaintenance] = ("إغلاق صيانة", "يغلق الأمر ويحدّث العدّاد.", "الأمر مفتوح؛ قد يصفّر العدّاد حسب الإعداد.", "UPDATE"),
            [ActionCodes.Checkout] = ("تحصيل POS", "ينشئ حجزًا/فاتورة وسندات دفع.", "سلة غير فارغة؛ طرق دفع كافية؛ مخزون متاح.", "bookings:INSERT"),
            [ActionCodes.Complete] = ("إنهاء متابعة", "يغلق متابعة العميل.", "المتابعة مفتوحة.", "UPDATE"),
            [ActionCodes.Approve] = ("اعتماد", "يعتمد تسوية/مستند.", "صلاحية مدير؛ الحالة مسودة.", "UPDATE"),
            [ActionCodes.Calculate] = ("حساب", "يحسب عمولات الشهر.", "فترة/أساس محدد؛ يمنع التكرار.", "INSERT"),
            [ActionCodes.SetMain] = ("تعيين رئيسية", "يجعل الخزينة رئيسية.", "خزينة نشطة؛ رئيسية واحدة.", "UPDATE"),
            [ActionCodes.ManageMethods] = ("إدارة الطرق", "إضافة طرق دفع/عمولات.", "الخزينة نشطة؛ طريقة واحدة على الأقل.", "INSERT"),
            [ActionCodes.ManageItems] = ("إدارة الأسعار", "إضافة أصناف/شرائح لقائمة الأسعار.", "الأسعار غير سالبة؛ القائمة نشطة.", "UPDATE"),
            [ActionCodes.ManageDefaults] = ("إعدادات الفرع", "ربط الحسابات الافتراضية.", "حسابات قابلة للترحيل ونشطة.", "UPDATE"),
            [ActionCodes.AssignOthers] = ("إسناد لموظف", "يسند الحجز لموظف آخر.", "الموظف نشط؛ الحجز غير ملغي.", "INSERT"),
            [ActionCodes.Execute] = ("تنفيذ", "ينشئ إذن تنفيذ.", "الحجز مؤكد وله كميات متبقية.", "INSERT"),
            [ActionCodes.SaveDraft] = ("حفظ مسودة", "يحفظ دون ترحيل.", "توازن القيد مطلوب للترحيل وليس للمسودة.", "INSERT/UPDATE"),
            [ActionCodes.AddLine] = ("إضافة سطر", "يضيف بندًا في الذاكرة.", "لا يُحفظ إلا بزر الحفظ.", "لا شيء"),
            [ActionCodes.DeleteLine] = ("حذف سطر", "يحذف بندًا من الذاكرة.", "لا يُحفظ إلا بزر الحفظ.", "لا شيء"),
            [ActionCodes.AutoBalance] = ("موازنة تلقائية", "يضيف سطر الموازنة.", "يعمل داخل النموذج فقط.", "لا شيء"),
            [ActionCodes.DistributeCost] = ("توزيع التكلفة", "يوزّع سطرًا على مراكز التكلفة.", "مجموع التوزيع = مبلغ السطر.", "لا شيء"),
            [ActionCodes.SelectParty] = ("اختيار طرف", "يفتح منتقي الطرف.", "طرف نشط.", "لا شيء"),
            [ActionCodes.Open] = ("فتح", "يفتح السجل.", "حسب الصلاحية/الفرع.", "لا شيء"),
            [ActionCodes.Copy] = ("نسخ", "يفتح نسخة جديدة.", "لا ينسخ القيود المرحّلة.", "INSERT عند الحفظ"),
            [ActionCodes.OpenEntry] = ("فتح القيد", "ينتقل لقيد اليومية.", "القيد موجود.", "لا شيء"),
            [ActionCodes.CreateRoot] = ("إضافة رئيسي", "ينشئ عقدة جذر.", "الكود فريد.", "INSERT"),
            [ActionCodes.CreateChild] = ("إضافة فرعي", "ينشئ عقدة تحت الأب.", "الأب موجود وغير مرحّل عليه.", "INSERT"),
            [ActionCodes.Move] = ("نقل", "يغيّر الأب.", "لا يخلق دورة في الشجرة.", "UPDATE"),
            [ActionCodes.LoadDefaultTree] = ("تحميل الشجرة", "ينشئ الشجرة القياسية.", "لا يكرر الحسابات الموجودة.", "INSERT"),
            [ActionCodes.Save] = ("حفظ", "يحفظ الإعدادات.", "تحقق من الحقول/الحسابات.", "UPDATE"),
            [ActionCodes.SaveFilter] = ("حفظ فلتر", "يحفظ فلاتر التقرير.", "اسم فريد لكل مستخدم/تقرير.", "saved_report_filters"),
            [ActionCodes.DeleteFilter] = ("حذف فلتر", "يحذف فلترًا محفوظًا.", "ملكية المستخدم.", "saved_report_filters"),
            [ActionCodes.MarkAsRead] = ("تعليم كمقروء", "يعلّم الإشعارات كمقروءة.", "إشعارات المستخدم فقط.", "notifications"),
            [ActionCodes.ClearAll] = ("مسح الكل", "يمسح إشعارات المستخدم.", "لا يمس إشعارات غيره.", "notifications"),
            [ActionCodes.RunTests] = ("تشغيل الاختبارات", "ينفّذ اختبارات الجودة.", "قد يحتاج قاعدة QA منفصلة.", "qa_test_runs"),
            [ActionCodes.Confirm] = ("تأكيد", "يثبت الحجز ويحجز الكميات.", "الكميات متاحة؛ الحجز غير ملغي.", "UPDATE"),
            [ActionCodes.Manage] = ("إدارة", "إدارة المواعيد/الحالة.", "لا تعارض لنفس الموظف.", "UPDATE"),
            [ActionCodes.Close] = ("إغلاق", "يغلق الوردية/السجل.", "وردية واحدة مفتوحة لكل خزينة.", "UPDATE"),
            [ActionCodes.Reopen] = ("إعادة فتح", "يعيد فتح وردية مغلقة.", "سبب مطلوب.", "UPDATE"),
            [ActionCodes.Reconcile] = ("مطابقة", "يعرض فروق المستندات/الأستاذ.", "قراءة فقط.", "لا شيء"),
            [ActionCodes.ViewUnposted] = ("عرض غير المرحّل", "يعرض المستندات المرحّلة بلا قيد.", "قراءة فقط.", "لا شيء")
        };

    private static void AppendActionGuide(StringBuilder md, string language)
    {
        md.AppendLine("## 6. قيود الأزرار واستخدامها");
        md.AppendLine();
        md.AppendLine("| الكود | الاسم | ماذا يفعل | القيود | السجل |");
        md.AppendLine("|---|---|---|---|---|");
        foreach (var action in AppRegistry.Screens.SelectMany(s => s.Actions)
                     .Select(a => a.Code).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var info = Guide.TryGetValue(action, out var guide)
                ? guide
                : (action, "إجراء مسجل في AppRegistry.", "راجع الخدمة/الشاشة.", "حسب الخدمة");
            md.AppendLine($"| `{action}` | {info.Item1} | {info.Item2} | {info.Item3} | {info.Item4} |");
        }
        md.AppendLine();
    }

    private static void AppendAuditAppendix(StringBuilder md, ManualSourceSet source, string language)
    {
        md.AppendLine("## 8. سجل التدقيق");
        md.AppendLine();
        md.AppendLine("| الإجراء | المعنى |");
        md.AppendLine("|---|---|");
        md.AppendLine("| `INSERT` | إنشاء سجل |");
        md.AppendLine("| `UPDATE` | تعديل/ترحيل/عكس/تعطيل |");
        md.AppendLine("| `DELETE` | حذف نهائي |");
        md.AppendLine("| `LOGIN` / `LOGOUT` / `LOGIN_FAILED` | الدخول/الخروج/محاولة فاشلة |");
        md.AppendLine("| `PASSWORD_RESET` | تغيير/إعادة تعيين كلمة مرور |");
        md.AppendLine("| `PERMISSION_CHANGE` | تغيير صلاحيات |");
        md.AppendLine("| `MIGRATION` | تنفيذ ترحيلات |");
        md.AppendLine();

        if (source.Services.Count == 0)
        {
            md.AppendLine("> ملفات الخدمات غير متاحة في نسخة النشر؛ راجع شاشة سجل الأنشطة لمعرفة الجداول/الإجراءات الفعلية.");
            return;
        }

        md.AppendLine("### أين تكتب الخدمات في audit_log");
        md.AppendLine();
        md.AppendLine("| الخدمة | الجدول | الإجراء |");
        md.AppendLine("|---|---|---|");
        var pattern = new Regex(
            "_audit\\.WriteAsync\\(\\s*actor\\s*,\\s*\"([^\"]+)\"\\s*,[^;]*?\"([A-Z_]+)\"",
            RegexOptions.Singleline | RegexOptions.Compiled);
        foreach (var file in source.Services.OrderBy(f => f.RelativePath, StringComparer.OrdinalIgnoreCase))
        {
            foreach (Match match in pattern.Matches(file.Text))
                md.AppendLine($"| `{Path.GetFileNameWithoutExtension(file.RelativePath)}` | " +
                              $"`{match.Groups[1].Value}` | `{match.Groups[2].Value}` |");
        }
        md.AppendLine();
    }
}
