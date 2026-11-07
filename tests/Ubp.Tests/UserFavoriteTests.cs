using Ubp.Core.Results;
using Ubp.Core.Security;
using Xunit;

namespace Ubp.Tests;

/// <summary>مفضلة الشاشات: إضافة/إزالة مع منع التكرار والتحقق من صلاحية الشاشة.</summary>
public sealed class UserFavoriteTests : IClassFixture<TestDb>
{
    private readonly TestDb _db;

    public UserFavoriteTests(TestDb db) => _db = db;

    [Fact]
    public async Task Toggle_adds_and_removes_a_screen_without_duplicates()
    {
        var actor = await _db.AdminActorAsync();
        var svc = _db.UserFavorites();

        var added = await svc.ToggleAsync(actor, ScreenCodes.Items);
        Assert.True(added);
        Assert.Contains(ScreenCodes.Items, await svc.ListAsync(actor));
        Assert.Single((await svc.ListAsync(actor)).Where(c => c == ScreenCodes.Items));

        var removed = await svc.ToggleAsync(actor, ScreenCodes.Items);
        Assert.False(removed);
        Assert.DoesNotContain(ScreenCodes.Items, await svc.ListAsync(actor));
    }

    [Fact]
    public async Task Unknown_screen_cannot_be_favorited()
    {
        var actor = await _db.AdminActorAsync();
        var error = await Assert.ThrowsAsync<ValidationAppException>(() =>
            _db.UserFavorites().ToggleAsync(actor, "NOT_A_SCREEN"));
        Assert.Equal("validation.required_fields", error.Code);
    }
}
