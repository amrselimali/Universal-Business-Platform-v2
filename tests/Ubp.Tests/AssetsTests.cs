using Dapper;
using Npgsql;
using Ubp.Core.Models;
using Ubp.Core.Paging;
using Ubp.Core.Results;
using Xunit;

namespace Ubp.Tests;

/// <summary>
/// 8أ: الأصول الثابتة — أصل لكل فرع مع إمكانية «مشترك»، عدّاد تصاعدي يُصفَّر عند الصيانة
/// فقط إذا كان مُعدّاً على الأصل، والأصول بلا عدّاد لا تُتابع بالقراءات.
/// </summary>
public sealed class AssetsTests : IClassFixture<TestDb>
{
    private readonly TestDb _db;

    public AssetsTests(TestDb db) => _db = db;

    private async Task<Guid> CreateCategoryAsync(bool metered)
    {
        await using var connection = new NpgsqlConnection(TestDb.AdminConnection);
        await connection.OpenAsync();
        return await connection.ExecuteScalarAsync<Guid>("""
            INSERT INTO asset_categories (code, name_ar, is_metered, useful_life_months)
            VALUES (@code, @name, @metered, 60) RETURNING id
            """, new { code = "AC" + Guid.NewGuid().ToString("N")[..6], name = "تصنيف", metered });
    }

    [Fact]
    public async Task Branch_scope_includes_own_and_shared_assets_only()
    {
        var actor = await _db.AdminActorAsync();
        var category = await CreateCategoryAsync(false);
        var mine = await _db.CreateBranchAsync("AS-B1", "فرع الأصول");
        var other = await _db.CreateBranchAsync("AS-B2", "فرع آخر");

        await _db.Assets().CreateAsync(actor, new AssetInput
        {
            Code = "AS-1", NameAr = "أصل الفرع", CategoryId = category, BranchId = mine,
            Cost = 1000
        });
        await _db.Assets().CreateAsync(actor, new AssetInput
        {
            Code = "AS-SHARED", NameAr = "أصل مشترك", CategoryId = category, BranchId = null,
            Cost = 500
        });

        var repo = new Ubp.Infrastructure.Repositories.AssetRepository(_db.Factory);
        var mineList = await repo.ListAsync(new PageRequest(), [mine], null, null, false);
        var otherList = await repo.ListAsync(new PageRequest(), [other], null, null, false);

        Assert.Contains(mineList.Items, a => a.Code == "AS-1");
        Assert.Contains(mineList.Items, a => a.Code == "AS-SHARED");
        Assert.DoesNotContain(otherList.Items, a => a.Code == "AS-1");
        Assert.Contains(otherList.Items, a => a.Code == "AS-SHARED");
        Assert.True(mineList.Items.Single(a => a.Code == "AS-SHARED").IsShared);
    }

    [Fact]
    public async Task Meter_readings_must_increase_and_update_the_current_value()
    {
        var actor = await _db.AdminActorAsync();
        var category = await CreateCategoryAsync(true);
        var asset = await _db.Assets().CreateAsync(actor, new AssetInput
        {
            Code = "AS-M1", NameAr = "ماكينة بعداد", CategoryId = category,
            Cost = 2000, IsMetered = true, MeterType = MeterTypes.Copies,
            MeterResetsOnMaintenance = true
        });

        var first = await _db.Assets().AddReadingAsync(actor, asset.Id, 100);
        Assert.Equal(100m, first.Value);
        Assert.Equal(100m, first.Delta);

        var second = await _db.Assets().AddReadingAsync(actor, asset.Id, 150);
        Assert.Equal(50m, second.Delta);
        Assert.Equal(150m, (await _db.Assets().GetAsync(actor, asset.Id)).CurrentMeterValue);

        var error = await Assert.ThrowsAsync<ValidationAppException>(async () =>
            await _db.Assets().AddReadingAsync(actor, asset.Id, 120));
        Assert.Equal("validation.meter_must_increase", error.Code);
    }

    [Fact]
    public async Task Non_metered_assets_reject_readings()
    {
        var actor = await _db.AdminActorAsync();
        var category = await CreateCategoryAsync(false);
        var asset = await _db.Assets().CreateAsync(actor, new AssetInput
        {
            Code = "AS-NM", NameAr = "أصل بلا عدّاد", CategoryId = category, Cost = 300
        });

        var error = await Assert.ThrowsAsync<ValidationAppException>(async () =>
            await _db.Assets().AddReadingAsync(actor, asset.Id, 10));
        Assert.Equal("validation.asset_not_metered", error.Code);
    }

    [Fact]
    public async Task Maintenance_close_resets_the_meter_only_when_configured()
    {
        var actor = await _db.AdminActorAsync();
        var category = await CreateCategoryAsync(true);

        var resetAsset = await _db.Assets().CreateAsync(actor, new AssetInput
        {
            Code = "AS-RST", NameAr = "بعداد يُصفَّر", CategoryId = category, Cost = 1000,
            IsMetered = true, MeterType = MeterTypes.Pages, MeterResetsOnMaintenance = true
        });
        var keepAsset = await _db.Assets().CreateAsync(actor, new AssetInput
        {
            Code = "AS-KEEP", NameAr = "بعداد لا يُصفَّر", CategoryId = category, Cost = 1000,
            IsMetered = true, MeterType = MeterTypes.Pages, MeterResetsOnMaintenance = false
        });

        await _db.Assets().AddReadingAsync(actor, resetAsset.Id, 500);
        await _db.Assets().AddReadingAsync(actor, keepAsset.Id, 700);

        var resetOrder = await _db.Assets().OpenOrderAsync(actor, new MaintenanceOrderInput(
            resetAsset.Id, null, DateTime.Today, 50, "PREVENTIVE", "صيانة دورية"));
        var keepOrder = await _db.Assets().OpenOrderAsync(actor, new MaintenanceOrderInput(
            keepAsset.Id, null, DateTime.Today, 30, "CORRECTIVE", null));

        await _db.Assets().CloseOrderAsync(actor, resetOrder);
        await _db.Assets().CloseOrderAsync(actor, keepOrder);

        Assert.Equal(0m, (await _db.Assets().GetAsync(actor, resetAsset.Id)).CurrentMeterValue);
        Assert.Equal(700m, (await _db.Assets().GetAsync(actor, keepAsset.Id)).CurrentMeterValue);

        var readings = await _db.Assets().ReadingsAsync(actor, resetAsset.Id);
        Assert.Contains(readings, r => r.Source == MeterSources.MaintenanceReset);

        // إغلاق نفس الأمر مرتين مرفوض
        await Assert.ThrowsAsync<ValidationAppException>(async () =>
            await _db.Assets().CloseOrderAsync(actor, resetOrder));
    }
}
