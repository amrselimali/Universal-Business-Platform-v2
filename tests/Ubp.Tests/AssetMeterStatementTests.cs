using Dapper;
using Npgsql;
using Ubp.Core.Models;
using Ubp.Core.Results;
using Xunit;

namespace Ubp.Tests;

/// <summary>
/// متابعة عدّاد الأصل: إجماليات بالشهر/اليوم/يوم محدد (بداية المدة + المستهلك + التسويات +
/// الرصيد)، وحركات القراءات والتسويات، وإضافة تسوية لعدّاد الجهاز.
/// </summary>
public sealed class AssetMeterStatementTests : IClassFixture<TestDb>
{
    private readonly TestDb _db;

    public AssetMeterStatementTests(TestDb db) => _db = db;

    private static async Task<Guid> CreateCategoryAsync()
    {
        await using var connection = new NpgsqlConnection(TestDb.AdminConnection);
        await connection.OpenAsync();
        return await connection.ExecuteScalarAsync<Guid>("""
            INSERT INTO asset_categories (code, name_ar, is_metered, useful_life_months)
            VALUES (@code, @name, true, 60) RETURNING id
            """, new { code = "MC" + Guid.NewGuid().ToString("N")[..6], name = "تصنيف عدّاد" });
    }

    private async Task<FixedAsset> CreateAssetAsync(string code)
    {
        var actor = await _db.AdminActorAsync();
        var category = await CreateCategoryAsync();
        return await _db.Assets().CreateAsync(actor, new AssetInput
        {
            Code = code, NameAr = "ماكينة متابعة عدّاد", CategoryId = category, Cost = 1000,
            IsMetered = true, MeterType = MeterTypes.Copies
        });
    }

    private static async Task AddReadingRowAsync(Guid assetId, DateTimeOffset at, decimal value,
        decimal delta)
    {
        await using var connection = new NpgsqlConnection(TestDb.AdminConnection);
        await connection.OpenAsync();
        await connection.ExecuteAsync("""
            INSERT INTO asset_meter_readings (asset_id, reading_at, value, delta, source)
            VALUES (@assetId, @at, @value, @delta, 'MANUAL')
            """, new { assetId, at, value, delta });
    }

    [Fact]
    public async Task Monthly_statement_reports_opening_consumed_adjustments_and_balance()
    {
        var actor = await _db.AdminActorAsync();
        var asset = await CreateAssetAsync("MC-1");

        var thisMonth = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
        var lastMonth = thisMonth.AddMonths(-1);

        await AddReadingRowAsync(asset.Id, new DateTimeOffset(lastMonth.AddDays(14).AddHours(12)),
            100, 100);
        await AddReadingRowAsync(asset.Id, new DateTimeOffset(thisMonth.AddDays(4).AddHours(12)),
            150, 50);
        await AddReadingRowAsync(asset.Id, new DateTimeOffset(thisMonth.AddDays(20).AddHours(12)),
            180, 30);

        await _db.Assets().AddMeterAdjustmentAsync(actor, new AssetMeterAdjustmentInput
        {
            AssetId = asset.Id, AdjustedAt = thisMonth.AddDays(10), Units = 20, Note = "زيادة جرد"
        });
        await _db.Assets().AddMeterAdjustmentAsync(actor, new AssetMeterAdjustmentInput
        {
            AssetId = asset.Id, AdjustedAt = thisMonth.AddDays(25), Units = -5, Note = "خصم خطأ قراءة"
        });

        var statement = await _db.Assets().MeterStatementAsync(actor, asset.Id,
            MeterStatementModes.Month, lastMonth, thisMonth);

        Assert.Equal(2, statement.Rows.Count);
        var previous = statement.Rows[0];
        Assert.Equal(0m, previous.Opening);
        Assert.Equal(100m, previous.Consumed);
        Assert.Equal(100m, previous.Closing);

        var current = statement.Rows[1];
        Assert.Equal(100m, current.Opening);
        Assert.Equal(80m, current.Consumed);
        Assert.Equal(15m, current.Adjustments);
        Assert.Equal(195m, current.Closing);
        Assert.Contains("زيادة جرد", current.AdjustmentNotes);
        Assert.Contains("خصم خطأ قراءة", current.AdjustmentNotes);

        Assert.Equal(5, statement.Movements.Count);
        Assert.Contains(statement.Movements, m => m.IsAdjustment && m.Units == 20m);
        Assert.Contains(statement.Movements, m => !m.IsAdjustment && m.Value == 180m);
    }

    [Fact]
    public async Task Single_day_statement_and_adjustment_validation()
    {
        var actor = await _db.AdminActorAsync();
        var asset = await CreateAssetAsync("MC-2");
        var today = DateTime.Today;

        await AddReadingRowAsync(asset.Id, new DateTimeOffset(today.AddHours(10)), 40, 40);

        var statement = await _db.Assets().MeterStatementAsync(actor, asset.Id,
            MeterStatementModes.SingleDay, today);
        Assert.Single(statement.Rows);
        Assert.Equal(40m, statement.Rows[0].Consumed);
        Assert.Single(statement.Movements);

        var noUnits = await Assert.ThrowsAsync<ValidationAppException>(() =>
            _db.Assets().AddMeterAdjustmentAsync(actor, new AssetMeterAdjustmentInput
            { AssetId = asset.Id, Units = 0, Note = "بدون وحدات" }));
        Assert.Equal("validation.meter_adjustment_units", noUnits.Code);

        var noNote = await Assert.ThrowsAsync<ValidationAppException>(() =>
            _db.Assets().AddMeterAdjustmentAsync(actor, new AssetMeterAdjustmentInput
            { AssetId = asset.Id, Units = 3 }));
        Assert.Equal("validation.meter_adjustment_note", noNote.Code);
    }
}
