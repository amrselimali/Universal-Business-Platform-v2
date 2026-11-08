using System.Linq;
using Ubp.Core.Security;
using Xunit;

namespace Ubp.Tests;

/// <summary>سجل الصلاحيات: كل شاشة لها VIEW، وكل زر حساس له صلاحية قابلة للمنح.</summary>
public sealed class PermissionRegistryTests
{
    [Fact]
    public void Every_screen_exposes_a_view_permission()
    {
        foreach (var screen in AppRegistry.Screens)
            Assert.Contains(ActionCodes.View, screen.Actions.Select(a => a.Code));
    }

    [Fact]
    public void Shift_data_edit_is_a_grantable_permission()
    {
        var shifts = AppRegistry.ByCode[ScreenCodes.TreasuryShifts];
        Assert.Contains(ActionCodes.Edit, shifts.Actions.Select(a => a.Code));
        // عمليات الشفت الأساسية تبقى قابلة للمنح.
        foreach (var code in new[]
                 { ActionCodes.Open, ActionCodes.Close, ActionCodes.Reopen, ActionCodes.Approve })
            Assert.Contains(code, shifts.Actions.Select(a => a.Code));
    }

    [Fact]
    public void Pos_checkout_and_database_edit_rows_are_grantable()
    {
        Assert.Contains(ActionCodes.Checkout,
            AppRegistry.ByCode[ScreenCodes.PosTerminal].Actions.Select(a => a.Code));
        Assert.Contains(ActionCodes.EditRows,
            AppRegistry.ByCode[ScreenCodes.DatabaseAdmin].Actions.Select(a => a.Code));
    }
}
