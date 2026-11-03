using Ubp.Core.Security;

namespace Ubp.Web.Components.Layout;

/// <summary>
/// ترتيب عرض القائمة الجانبية فقط (لا يغيّر سجل الشاشات ولا الصلاحيات ولا المسارات):
/// تبدأ مجموعة المبيعات بالحجوزات (حجز موعد) ثم متابعات العملاء ثم نقطة البيع ثم الخدمات
/// المحجوزة، وبعدها بقية الشاشات بنفس ترتيب السجل الرسمي (ترتيب مستقر).
/// </summary>
public static class NavOrder
{
    private static readonly string[] SalesPriority =
    {
        ScreenCodes.SalesBookings, ScreenCodes.FollowUps, ScreenCodes.PosTerminal,
        ScreenCodes.Reservations
    };

    /// <summary>يعيد نفس الشاشات بترتيب العرض، مع الحفاظ على ترتيب السجل لغير المذكورين.</summary>
    public static IReadOnlyList<ScreenDefinition> Apply(IReadOnlyList<ScreenDefinition> screens) =>
        screens.OrderBy(Rank).ToList();

    private static int Rank(ScreenDefinition screen)
    {
        var index = Array.IndexOf(SalesPriority, screen.Code);
        return index < 0 ? SalesPriority.Length : index;
    }
}
