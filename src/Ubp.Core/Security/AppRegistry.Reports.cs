using static Ubp.Core.Security.ScreenCodes;
using static Ubp.Core.Security.ActionCodes;

namespace Ubp.Core.Security;

/// <summary>شاشات مجموعة التقارير (مفصولة للحفاظ على سقف 300 سطر للملف).</summary>
public static partial class AppRegistry
{
    private static ScreenAction R(string code) => new(code, $"action.{code.ToLowerInvariant()}");

    // خاصية (لا حقل) حتى لا يعتمد ترتيب التهيئة الساكنة على ترتيب الملفات في partial class.
    private static IReadOnlyList<ScreenDefinition> ReportScreens => new List<ScreenDefinition>
    {
        new(StockReports, "/reports/stock", "Assessment", "screen.stock_reports", GroupReports,
            new[] { R(View) }),

        new(ScreenCodes.ReportsCenter, "/reports/center", "QueryStats", "screen.reports_center",
            GroupReports,
            new[] { R(View), R(SaveFilter), R(DeleteFilter), R(ExportCsv), R(ExportXlsx), R(Print) })
    };
}
