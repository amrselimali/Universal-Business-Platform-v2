using Xunit;

namespace Ubp.Tests;

/// <summary>تدوير الـ Refresh Token: الاستخدام الثاني للتوكن القديم يجب أن يُرفض.</summary>
public sealed class RefreshTokenTests : IClassFixture<TestDb>
{
    private readonly TestDb _db;

    public RefreshTokenTests(TestDb db) => _db = db;

    [Fact]
    public async Task Refresh_token_cannot_be_used_twice()
    {
        var auth = _db.Auth();
        var login = await auth.LoginAsync("admin", TestDb.AdminPassword, "127.0.0.1", "rt-1");
        Assert.True(login.Success);

        var rotated = await auth.RefreshAsync(login.RefreshToken!, "127.0.0.1", "rt-2");
        Assert.True(rotated.Success);
        Assert.NotEqual(login.RefreshToken, rotated.RefreshToken);

        var reused = await auth.RefreshAsync(login.RefreshToken!, "127.0.0.1", "rt-3");
        Assert.False(reused.Success);
        Assert.Equal("auth.invalid_refresh", reused.ErrorKey);
    }

    [Fact]
    public async Task Refresh_after_logout_does_not_issue_new_tokens()
    {
        var auth = _db.Auth();
        var login = await auth.LoginAsync("admin", TestDb.AdminPassword, "127.0.0.1", "rt-4");
        Assert.True(login.Success);

        await auth.LogoutAsync(login.SessionId!.Value, login.User!.Id, null, "127.0.0.1", "rt-5");

        var refreshed = await auth.RefreshAsync(login.RefreshToken!, "127.0.0.1", "rt-6");
        Assert.False(refreshed.Success);
    }
}
