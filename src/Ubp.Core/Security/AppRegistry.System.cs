using static Ubp.Core.Security.ScreenCodes;
using static Ubp.Core.Security.ActionCodes;

namespace Ubp.Core.Security;

/// <summary>شاشات النظام/التشغيل (مفصولة للحفاظ على سقف 300 سطر للملف).</summary>
public static partial class AppRegistry
{
    private static ScreenAction S(string code) => new(code, $"action.{code.ToLowerInvariant()}");

    // خاصية (لا حقل) حتى لا يعتمد ترتيب التهيئة الساكنة على ترتيب الملفات في partial class.
    private static IReadOnlyList<ScreenDefinition> SystemScreens => new List<ScreenDefinition>
    {
        new(SystemHealth, "/admin/system-health", "MonitorHeart", "screen.system_health",
            GroupSystem, new[] { S(View) }),

        new(ScreenCodes.ImportTool, "/admin/import-tool", "UploadFile", "screen.import_tool",
            GroupSystem, new[] { S(View), S(Preview), S(ImportCsv) })
    };
}
