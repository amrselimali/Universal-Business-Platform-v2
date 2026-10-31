using Microsoft.AspNetCore.Http;
using Ubp.Core.Security;
using Ubp.Infrastructure.Security;
using Ubp.Web.Security;
using Xunit;

namespace Ubp.Tests;

/// <summary>
/// إجبار تغيير كلمة المرور عند أول دخول (الترحيل 0036):
/// العلم يُضبط للادمن المزروع، ويُرفع بعد التغيير، والبوابة تحجب كل مسار آخر.
/// </summary>
public sealed class PasswordChangeTests : IClassFixture<TestDb>
{
    private const string NewPassword = "New-Str0ng!2026";
    private readonly TestDb _db;

    public PasswordChangeTests(TestDb db) => _db = db;

    [Fact]
    public async Task Seeded_admin_must_change_the_password_at_first_login()
    {
        // لا نغيّر كلمة مرور الأدمن هنا: مجموعة الاختبارات تشترك في نفس قاعدة البيانات.
        var result = await _db.Auth().LoginAsync("admin", TestDb.AdminPassword, "127.0.0.1", "t1");
        Assert.True(result.Success);
        Assert.True(result.MustChangePassword);
    }

    [Fact]
    public async Task Changing_the_password_clears_the_flag_and_invalidates_the_old_one()
    {
        var branch = await _db.CreateBranchAsync("PC-B2", "فرع تغيير الكلمة");
        var id = await _db.CreateUserAsync("change.target", "Str0ng!Start2026", [branch]);
        await _db.UserRepo.SetPasswordAsync(id, new PasswordHasher().Hash("Str0ng!Start2026"), true);

        var auth = _db.Auth();
        var login = await auth.LoginAsync("change.target", "Str0ng!Start2026", "127.0.0.1", "t2");
        Assert.True(login.Success);
        Assert.True(login.MustChangePassword);

        var actor = new ActorContext
        {
            UserId = id, Username = "change.target", Language = "ar", TraceId = "test-trace"
        };
        await auth.ChangePasswordAsync(actor, "Str0ng!Start2026", NewPassword);

        var oldLogin = await auth.LoginAsync("change.target", "Str0ng!Start2026", "127.0.0.1", "t3");
        Assert.False(oldLogin.Success);

        var newLogin = await auth.LoginAsync("change.target", NewPassword, "127.0.0.1", "t4");
        Assert.True(newLogin.Success);
        Assert.False(newLogin.MustChangePassword);
    }

    [Fact]
    public async Task Admin_reset_requires_a_change_at_the_next_login()
    {
        var branch = await _db.CreateBranchAsync("PC-B1", "فرع كلمة المرور");
        var id = await _db.CreateUserAsync("reset.target", "Str0ng!Start2026", [branch]);
        await _db.UserRepo.SetPasswordAsync(id, new PasswordHasher().Hash("Tmp-Str0ng!2026"), true);

        var result = await _db.Auth().LoginAsync("reset.target", "Tmp-Str0ng!2026", "127.0.0.1", "t5");
        Assert.True(result.Success);
        Assert.True(result.MustChangePassword);
    }

    [Theory]
    [InlineData("/inventory/items", true)]
    [InlineData("/change-password", false)]
    [InlineData("/css/app.css", false)]
    [InlineData("/_blazor/negotiate", false)]
    [InlineData("/api/v1/auth/logout", false)]
    public void Gate_blocks_everything_except_the_change_password_screen(string path, bool blocked)
    {
        // HttpRequest.Path لا يحمل QueryString، لذلك لا نختبر حالة "?..." هنا.
        Assert.Equal(blocked, PasswordChangeGate.IsBlocked(new PathString(path), true));
    }

    [Fact]
    public void Gate_never_blocks_when_the_flag_is_clear()
    {
        Assert.False(PasswordChangeGate.IsBlocked(new PathString("/inventory/items"), false));
        Assert.False(PasswordChangeGate.IsBlocked(new PathString("/api/v1/system/me"), false));
    }

    [Fact]
    public void Change_password_route_detection_ignores_query_and_case()
    {
        Assert.True(PasswordChangeGate.IsChangePasswordRoute("change-password"));
        Assert.True(PasswordChangeGate.IsChangePasswordRoute("Change-Password?x=1"));
        Assert.False(PasswordChangeGate.IsChangePasswordRoute("sales/bookings"));
    }
}
