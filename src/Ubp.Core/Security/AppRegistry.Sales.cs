using static Ubp.Core.Security.ScreenCodes;
using static Ubp.Core.Security.ActionCodes;

namespace Ubp.Core.Security;

/// <summary>شاشات المبيعات الإضافية (مفصولة للحفاظ على سقف 300 سطر للملف).</summary>
public static partial class AppRegistry
{
    private static ScreenAction SA(string code) => new(code, $"action.{code.ToLowerInvariant()}");

    // خاصية (لا حقل) حتى لا يعتمد ترتيب التهيئة الساكنة على ترتيب الملفات.
    private static IReadOnlyList<ScreenDefinition> SalesExtraScreens => new List<ScreenDefinition>
    {
        new(Reservations, "/sales/reservations", "BookmarkAdded", "screen.reservations", GroupSales,
            new[] { SA(View), SA(ConvertToAdvance), SA(ReleaseReservation), SA(ExtendReservation),
                    SA(ExportCsv), SA(Print) })
    };
}
