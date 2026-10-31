using System.Globalization;
using Ubp.Application.Services;
using Ubp.Core.Models;
using Ubp.Core.Results;
using Ubp.Core.Security;
using Xunit;

namespace Ubp.Tests;

/// <summary>
/// تنسيق العرض الموحّد (الترحيل 0037): تاريخ/وقت/أرقام من الإعدادات، وتحويل UTC إلى المنطقة المكوَّنة
/// بدل الاعتماد على توقيت السيرفر (كانت الحاوية UTC والشاشات تعرض توقيتين مختلفين).
/// </summary>
public sealed class DisplayFormatterTests
{
    private sealed class FakeSettings(AppSettings? snapshot) : ISettingsService
    {
        public AppSettings? CachedSnapshot { get; set; } = snapshot;
        public Task<AppSettings> GetAsync(CancellationToken ct = default) =>
            Task.FromResult(CachedSnapshot ?? new AppSettings());
        public Task<AppSettings> UpdateAsync(ActorContext actor, AppSettings input,
            CancellationToken ct = default) => throw new NotSupportedException();
        public Task<string?> ValidatePasswordAsync(string password, string username,
            CancellationToken ct = default) => Task.FromResult<string?>(null);
        public void Invalidate() { }
    }

    private static readonly AppSettings Cairo = new()
    {
        DateFormat = "dd/MM/yyyy", TimeFormat = "HH:mm", NumberFormat = "#,##0.00",
        TimeZone = "Africa/Cairo"
    };

    [Fact]
    public void Utc_is_converted_to_the_configured_zone()
    {
        var formatter = new DisplayFormatter(new FakeSettings(Cairo));
        var utc = new DateTimeOffset(2026, 1, 15, 10, 0, 0, TimeSpan.Zero);

        Assert.Equal("15/01/2026", formatter.Date(utc));
        Assert.Equal("12:00", formatter.Time(utc));
        Assert.Equal("15/01/2026 12:00", formatter.DateTime(utc));
    }

    [Fact]
    public void Empty_values_render_as_a_dash()
    {
        var formatter = new DisplayFormatter(new FakeSettings(Cairo));
        Assert.Equal("—", formatter.Date(null));
        Assert.Equal("—", formatter.DateTime(null));
        Assert.Equal("—", formatter.Number(null));
    }

    [Fact]
    public void Numbers_use_the_configured_format()
    {
        var previous = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        try
        {
            var formatter = new DisplayFormatter(new FakeSettings(Cairo));
            Assert.Equal("1,234.50", formatter.Number(1234.5m));
            Assert.Equal("1,234.57", formatter.Decimal(1234.567m));
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [Fact]
    public void Unknown_zone_falls_back_to_utc_instead_of_throwing()
    {
        var formatter = new DisplayFormatter(new FakeSettings(new AppSettings
        {
            TimeZone = "Mars/Olympus", DateFormat = "yyyy-MM-dd", TimeFormat = "HH:mm"
        }));
        var utc = new DateTimeOffset(2026, 1, 15, 10, 0, 0, TimeSpan.Zero);
        Assert.Equal(utc, formatter.ToUserTime(utc));
    }

    [Theory]
    [InlineData("Africa/Cairo", true)]
    [InlineData("Asia/Riyadh", true)]
    [InlineData("UTC", true)]
    [InlineData("Mars/Olympus", false)]
    [InlineData("", false)]
    public void Settings_reject_unknown_time_zones(string zone, bool valid)
    {
        Assert.Equal(valid, SettingsService.IsValidTimeZone(zone));
    }
}

/// <summary>دورة كاملة على قاعدة حقيقية: عمود time_zone موجود، ويُحفظ ويُرفض غير الصالح.</summary>
public sealed class DisplaySettingsTests : IClassFixture<TestDb>
{
    private readonly TestDb _db;

    public DisplaySettingsTests(TestDb db) => _db = db;

    [Fact]
    public async Task Time_zone_column_exists_and_round_trips()
    {
        var settings = await _db.Settings().GetAsync();
        Assert.False(string.IsNullOrWhiteSpace(settings.TimeZone));

        var input = settings.Clone();
        input.TimeZone = "Asia/Riyadh";

        var updated = await _db.Settings().UpdateAsync(await _db.AdminActorAsync(), input);
        Assert.Equal("Asia/Riyadh", updated.TimeZone);
        Assert.Equal("Asia/Riyadh", (await _db.Settings().GetAsync()).TimeZone);
    }

    [Fact]
    public async Task Invalid_time_zone_is_rejected_on_save()
    {
        var settings = await _db.Settings().GetAsync();
        var input = settings.Clone();
        input.TimeZone = "Mars/Olympus";

        var error = await Assert.ThrowsAsync<ValidationAppException>(async () =>
            await _db.Settings().UpdateAsync(await _db.AdminActorAsync(), input));
        Assert.Equal("validation.time_zone", error.Code);
    }
}
