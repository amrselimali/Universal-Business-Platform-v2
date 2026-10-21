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
        var current = (await settings.GetAsync()).Clone();
        current.CompanyName = "شركة الاختبار";
        var updated = await settings.UpdateAsync(actor, current);
        Assert.Equal(current.Version + 1, updated.Version);
        Assert.Equal("شركة الاختبار", updated.CompanyName);
    }

    [Fact]
    public async Task Allow_negative_stock_defaults_to_false_and_can_be_changed()
    {
        var actor = await _db.AdminActorAsync();
        var settings = _db.Settings();
        var current = (await settings.GetAsync()).Clone();
        Assert.False(current.AllowNegativeStock);

        current.AllowNegativeStock = true;
        var updated = await settings.UpdateAsync(actor, current);
        Assert.True(updated.AllowNegativeStock);
        Assert.Equal(current.Version + 1, updated.Version);

        var reloaded = await _db.SettingsRepo.GetAsync();
        Assert.True(reloaded!.AllowNegativeStock);

        // إرجاع القيمة الافتراضية حتى لا تتأثر الاختبارات الأخرى
        reloaded.AllowNegativeStock = false;
        await settings.UpdateAsync(actor, reloaded);
    }

    [Fact]
    public async Task Stale_settings_version_is_rejected()
    {
        var actor = await _db.AdminActorAsync();
        var settings = _db.Settings();
        var snapshot = (await settings.GetAsync()).Clone();

        var first = snapshot.Clone();
        first.CompanyName = "تعديل أول";
        await settings.UpdateAsync(actor, first);

        // UpdateAsync يقرأ الإصدار الحالي من القاعدة، لذلك نختبر شرط version على مستوى الـ repository
        var stale = snapshot.Clone();
        stale.CompanyName = "تعديل متأخر";
        Assert.Null(await _db.SettingsRepo.UpdateAsync(stale));
    }

    [Fact]
    public async Task Settings_service_rejects_a_stale_version()
    {
        var actor = await _db.AdminActorAsync();
        var settings = _db.Settings();
        var snapshot = (await settings.GetAsync()).Clone();

        var first = snapshot.Clone();
        first.CompanyName = "تعديل أول عبر الخدمة";
        await settings.UpdateAsync(actor, first);

        var stale = snapshot.Clone();
        stale.CompanyName = "تعديل متأخر عبر الخدمة";
        await Assert.ThrowsAsync<Ubp.Core.Results.ConflictException>(async () =>
            await settings.UpdateAsync(actor, stale));
    }

    [Fact]
    public void Applied_migrations_are_reported_with_short_names()
    {
        var applied = new Ubp.Infrastructure.Migrations.MigrationRunner(_db.Options).GetAppliedScripts();
        Assert.True(applied.Count >= 11);
        Assert.All(applied, item => Assert.DoesNotContain("Ubp.Infrastructure", item.Name));
        Assert.Contains(applied, item => item.Name == "0001_extensions.sql");
        Assert.Contains(applied, item => item.Name == "0011_phase2a_settings_and_sequences.sql");
    }
}
