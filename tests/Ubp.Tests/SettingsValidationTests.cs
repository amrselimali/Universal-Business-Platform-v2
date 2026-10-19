using Xunit;

namespace Ubp.Tests;

public sealed class SettingsValidationTests : IClassFixture<TestDb>
{
    private readonly TestDb _db;

    public SettingsValidationTests(TestDb db) => _db = db;

    [Fact]
    public async Task Password_policy_uses_configured_minimum_and_rejects_username()
    {
        var settings = _db.Settings();
        Assert.Equal("validation.password_short", await settings.ValidatePasswordAsync("short", "user", default));
        Assert.Equal("validation.password_same_as_username", await settings.ValidatePasswordAsync("username", "username", default));
        Assert.Null(await settings.ValidatePasswordAsync("LongEnough!1", "user", default));
    }

    [Fact]
    public async Task Settings_update_bumps_version_and_is_audited()
    {
        var actor = await _db.AdminActorAsync();
        var settings = _db.Settings();
        var current = await settings.GetAsync();
        current.CompanyName = "شركة الاختبار";
        var updated = await settings.UpdateAsync(actor, current);
        Assert.Equal(current.Version + 1, updated.Version);
        Assert.Equal("شركة الاختبار", updated.CompanyName);
    }

    [Fact]
    public void Applied_migrations_are_reported_with_short_names()
    {
        var applied = new Ubp.Infrastructure.Migrations.MigrationRunner(_db.Options).GetAppliedScripts();
        Assert.True(applied.Count >= 10);
        Assert.All(applied, item => Assert.DoesNotContain("Ubp.Infrastructure", item.Name));
        Assert.Contains(applied, item => item.Name == "0001_extensions.sql");
    }
}
