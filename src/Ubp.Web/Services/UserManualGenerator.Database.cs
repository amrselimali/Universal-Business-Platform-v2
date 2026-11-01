using System.Text;
using Ubp.Application.Services;
using Ubp.Core.Security;

namespace Ubp.Web.Services;

public sealed partial class UserManualGenerator
{
    private static readonly HashSet<string> CoreTables = new(StringComparer.OrdinalIgnoreCase)
    {
        "users", "roles", "role_permissions", "user_permissions", "branches", "warehouses",
        "items", "stock_documents", "stock_document_lines", "partners", "commercial_documents",
        "commercial_document_lines", "journal_entries", "journal_entry_lines", "gl_balances",
        "treasuries", "vouchers", "voucher_lines", "bookings", "booking_lines", "delivery_notes",
        "fixed_assets", "audit_log", "app_settings", "fiscal_periods", "period_scope_closures",
        "saved_report_filters"
    };

    private static readonly IReadOnlyDictionary<string, string> TablePurpose =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["users"] = "المستخدمون وأعلام المدير وكلمة المرور وحالة القفل.",
            ["roles"] = "الأدوار التي تجمع الصلاحيات.",
            ["role_permissions"] = "صلاحيات كل دور لكل شاشة/فعل ونطاقها.",
            ["user_permissions"] = "استثناءات فردية للمستخدم تتغلب على صلاحيات الأدوار.",
            ["branches"] = "الفروع ونطاق البيانات.",
            ["warehouses"] = "المخازن وربطها بالفروع.",
            ["items"] = "الأصناف ووحداتها وباركوداتها وضريبتها.",
            ["stock_documents"] = "مستندات الاستلام/الصرف/التحويل/التسوية.",
            ["stock_document_lines"] = "بنود مستندات المخزون والكميات/التكاليف/الدفعات.",
            ["partners"] = "العملاء والموردون وبياناتهم المالية.",
            ["commercial_documents"] = "فواتير الشراء/المبيعات والمرتجعات.",
            ["commercial_document_lines"] = "بنود الفواتير والضريبة والخصم.",
            ["journal_entries"] = "رؤوس قيود اليومية وحالتها.",
            ["journal_entry_lines"] = "سطور القيود (مدين/دائن/طرف/مركز تكلفة).",
            ["gl_balances"] = "أرصدة الأستاذ الشهرية المجمّعة.",
            ["treasuries"] = "الخزائن وحساباتها وطرق الدفع.",
            ["vouchers"] = "سندات القبض/الصرف/التحويل.",
            ["voucher_lines"] = "بنود السندات وطرق الدفع/المبالغ.",
            ["bookings"] = "الحجوزات كالتزامات وكمياتها.",
            ["booking_lines"] = "بنود الحجز والأسعار والكميات.",
            ["delivery_notes"] = "إذون التنفيذ وصرف المخزون.",
            ["fixed_assets"] = "الأصول الثابتة والعدّادات والإهلاك.",
            ["audit_log"] = "سجل غير قابل للتعديل لكل العمليات.",
            ["app_settings"] = "إعدادات الشركة والأمان والترحيل الافتراضي.",
            ["fiscal_periods"] = "الفترات المالية وإغلاقها.",
            ["period_scope_closures"] = "إغلاق الفترات على مستوى الفرع/الوحدة/الشركة.",
            ["saved_report_filters"] = "فلاتر التقارير المحفوظة لكل مستخدم."
        };

    private async Task<IReadOnlyList<ManualTableDoc>> LoadTablesAsync(ActorContext actor,
        CancellationToken ct)
    {
        try
        {
            var tables = await _schema.GetTablesAsync(actor, ct);
            var docs = new List<ManualTableDoc>(tables.Count);
            foreach (var table in tables.OrderBy(t => t.Name, StringComparer.OrdinalIgnoreCase))
            {
                IReadOnlyList<ManualColumnDoc> columns = [];
                if (CoreTables.Contains(table.Name))
                {
                    try
                    {
                        columns = (await _schema.GetColumnsAsync(actor, table.Name, ct))
                            .Take(50)
                            .Select(c => new ManualColumnDoc(c.Name, c.DataType, c.Nullable))
                            .ToList();
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Could not read columns for {Table}", table.Name);
                    }
                }
                docs.Add(new ManualTableDoc(table.Name, table.RowCount, columns));
            }
            return docs;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not read the database schema for the user manual");
            return [];
        }
    }

    private static void AppendDatabaseSection(StringBuilder md, IReadOnlyList<ManualTableDoc> tables,
        string language)
    {
        md.AppendLine("## 5. قاعدة البيانات");
        md.AppendLine();
        if (tables.Count == 0)
        {
            md.AppendLine("> تعذّر قراءة جداول قاعدة البيانات (القاعدة غير متاحة أو لا تملك صلاحية العرض). " +
                          "تحقق من الاتصال ثم اضغط **تحديث الدليل**.");
            return;
        }
        md.AppendLine($"إجمالي الجداول: **{tables.Count}**. الجداول الأساسية تُعرض بأعمدتها، وبقية الجداول بملخصها. " +
                      "كل الأعمدة متاحة من شاشة **إدارة قاعدة البيانات** لمن يملك صلاحيتها.");
        md.AppendLine();
        md.AppendLine("| الجدول | الصفوف | الأعمدة | الغرض |");
        md.AppendLine("|---|---:|---:|---|");
        foreach (var table in tables)
            md.AppendLine($"| `{table.Name}` | {table.RowCount} | {table.Columns.Count} | " +
                          $"{TablePurpose.GetValueOrDefault(table.Name, "جدول نظامي.")} |");
        md.AppendLine();
        foreach (var table in tables.Where(t => t.Columns.Count > 0))
        {
            md.AppendLine($"### `{table.Name}`");
            md.AppendLine();
            md.AppendLine(TablePurpose.GetValueOrDefault(table.Name, "جدول نظامي."));
            md.AppendLine();
            md.AppendLine("| العمود | النوع | Nullable |");
            md.AppendLine("|---|---|---|");
            foreach (var column in table.Columns)
                md.AppendLine($"| `{column.Name}` | `{column.DataType}` | {(column.Nullable ? "نعم" : "لا")} |");
            md.AppendLine();
        }
    }
}
