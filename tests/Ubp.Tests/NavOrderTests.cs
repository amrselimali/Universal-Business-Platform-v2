using System.Linq;
using Ubp.Core.Security;
using Ubp.Web.Components.Layout;
using Xunit;

namespace Ubp.Tests;

/// <summary>
/// ترتيب القائمة الجانبية: المبيعات تبدأ بالحجوزات ثم متابعات العملاء ثم نقطة البيع ثم
/// الخدمات المحجوزة، وبقية الشاشات والمجموعات تحافظ على ترتيب السجل الرسمي.
/// </summary>
public sealed class NavOrderTests
{
    [Fact]
    public void Sales_group_starts_with_shifts_then_bookings_followups_pos_reserved_services()
    {
        var sales = AppRegistry.Screens
            .Where(s => s.GroupKey == AppRegistry.GroupSales)
            .ToList();

        var ordered = NavOrder.Apply(sales).Select(s => s.Code).Take(5).ToArray();

        Assert.Equal(new[]
        {
            ScreenCodes.TreasuryShifts, ScreenCodes.SalesBookings, ScreenCodes.FollowUps,
            ScreenCodes.PosTerminal, ScreenCodes.Reservations
        }, ordered);
    }

    [Fact]
    public void Remaining_sales_screens_keep_their_registry_order()
    {
        var sales = AppRegistry.Screens
            .Where(s => s.GroupKey == AppRegistry.GroupSales).ToList();
        var priority = new[]
        {
            ScreenCodes.TreasuryShifts, ScreenCodes.SalesBookings, ScreenCodes.FollowUps,
            ScreenCodes.PosTerminal, ScreenCodes.Reservations
        };
        var expected = sales.Where(s => !priority.Contains(s.Code)).Select(s => s.Code);
        var actual = NavOrder.Apply(sales).Skip(priority.Length).Select(s => s.Code);

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void Other_groups_keep_their_registry_order()
    {
        foreach (var groupKey in new[]
                 {
                     AppRegistry.GroupMain, AppRegistry.GroupInventory, AppRegistry.GroupPartners,
                     AppRegistry.GroupAccounting, AppRegistry.GroupDefinitions,
                     AppRegistry.GroupSecurity, AppRegistry.GroupReports, AppRegistry.GroupSystem
                 })
        {
            var screens = AppRegistry.Screens.Where(s => s.GroupKey == groupKey).ToList();
            Assert.Equal(screens.Select(s => s.Code),
                NavOrder.Apply(screens).Select(s => s.Code));
        }
    }
}
