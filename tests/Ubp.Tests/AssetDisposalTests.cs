using Dapper;
using Npgsql;
using Ubp.Core.Models;
using Ubp.Core.Results;
using Xunit;

namespace Ubp.Tests;

/// <summary>
/// استبعاد الأصل (قيد ربح/خسارة) ومزامنة قراءات العدّاد من فواتير البيع — الدفعة الأخيرة من 8أ.
/// </summary>
public sealed class AssetDisposalTests : IClassFixture<TestDb>
{
    private readonly TestDb _db;

    public AssetDisposalTests(TestDb db) => _db = db;

    private async Task<(Guid Branch, AssetCategory Category, Guid AssetId)> SetupAsync(string suffix)
    {
        var actor = await _db.AdminActorAsync();
        var chart = await _db.SeedAccountingAsync();
        var postable = (await _db.AccountRepo.ListPostableAsync())
            .Where(a => !a.RequiresParty).ToList();
        var branch = await _db.CreateBranchAsync("AX-B-" + suffix, "فرع الاستبعاد " + suffix);

        var categoryId = await _db.Assets().CreateCategoryAsync(actor, new AssetCategoryInput
        {
            Code = "AX-C-" + suffix, NameAr = "تصنيف استبعاد", UsefulLifeMonths = 12,
            AssetAccountId = postable[0].Id, AccumAccountId = postable[1].Id,
            ExpenseAccountId = postable[2].Id
        });
        var category = (await _db.Assets().CategoriesAsync(actor)).First(c => c.Id == categoryId);

        var asset = await _db.Assets().CreateAsync(actor, new AssetInput
        {
            Code = "AX-A-" + suffix, NameAr = "أصل للاستبعاد", CategoryId = categoryId,
            BranchId = branch, Cost = 1000, InServiceDate = DateTime.Today
        });

        // خزينة الفرع (تُستخدم كمقابل نقدي عند البيع)
        var treasuryId = Guid.CreateVersion7();
        await _db.TreasuryRepo.CreateTreasuryAsync(treasuryId, new TreasuryInput
        {
            BranchId = branch, Code = "AX-T-" + suffix, NameAr = "خزينة " + suffix,
            TreasuryType = TreasuryTypes.Cash, AccountId = postable[3].Id
        }, actor.UserId);

        await _db.AccountingSettings().SetDefaultAsync(actor, "asset_disposal_gain", postable[0].Id);
        await _db.AccountingSettings().SetDefaultAsync(actor, "asset_disposal_loss", postable[1].Id);

        return (branch, category, asset.Id);
    }

    [Fact]
    public async Task Disposing_an_asset_posts_a_balanced_entry_and_marks_it_disposed()
    {
        var actor = await _db.AdminActorAsync();
        var (_, _, assetId) = await SetupAsync("1");

        var result = await _db.Assets().DisposeAsync(actor, assetId, 1200, DateTime.Today);

        Assert.True(result.Gain > 0);
        Assert.Equal(1200m, result.SaleAmount);

        var asset = await _db.Assets().GetAsync(actor, assetId);
        Assert.Equal(AssetStatuses.Disposed, asset.Status);

        await using var connection = new NpgsqlConnection(TestDb.AdminConnection);
        await connection.OpenAsync();
        var status = await connection.ExecuteScalarAsync<string>(
            "SELECT status FROM journal_entries WHERE id = @id", new { id = result.JournalEntryId });
        Assert.Equal(EntryStatuses.Posted, status);

        var balanced = await connection.ExecuteScalarAsync<decimal>("""
            SELECT ABS(SUM(debit) - SUM(credit)) FROM journal_entry_lines WHERE entry_id = @id
            """, new { id = result.JournalEntryId });
        Assert.Equal(0m, balanced);
    }

    [Fact]
    public async Task A_disposed_asset_cannot_be_disposed_again()
    {
        var actor = await _db.AdminActorAsync();
        var (_, _, assetId) = await SetupAsync("2");
        await _db.Assets().DisposeAsync(actor, assetId, 100, DateTime.Today);

        var error = await Assert.ThrowsAsync<ValidationAppException>(async () =>
            await _db.Assets().DisposeAsync(actor, assetId, 100, DateTime.Today));
        Assert.Equal("validation.asset_disposed", error.Code);
    }

    [Fact]
    public async Task Sales_sync_runs_and_is_idempotent_when_there_are_no_lines()
    {
        var actor = await _db.AdminActorAsync();
        var (branch, category, _) = await SetupAsync("3");

        var asset = await _db.Assets().CreateAsync(actor, new AssetInput
        {
            Code = "AX-M1", NameAr = "ماكينة مزامنة", CategoryId = category.Id, BranchId = branch,
            Cost = 1000, IsMetered = true, MeterType = MeterTypes.Pages
        });
        var item = await _db.CreateItemAsync("AX-IT-3", "صنف مقابل العدّاد");

        await using var connection = new NpgsqlConnection(TestDb.AdminConnection);
        await connection.OpenAsync();
        await connection.ExecuteAsync("UPDATE items SET metered_asset_id = @asset WHERE id = @item",
            new { asset = asset.Id, item = item.Id });

        // ربط الصنف بالأصل مثبت في القاعدة (مصدر واحد)
        var linked = await connection.ExecuteScalarAsync<Guid?>(
            "SELECT metered_asset_id FROM items WHERE id = @item", new { item = item.Id });
        Assert.Equal(asset.Id, linked);

        // لا أسطر بيع بعد ⇒ صفر، والتشغيل الثاني يبقى صفراً (idempotent)
        Assert.Equal(0, await _db.Assets().SyncSalesReadingsAsync(actor, DateTime.Today));
        Assert.Equal(0, await _db.Assets().SyncSalesReadingsAsync(actor, DateTime.Today));
        Assert.Equal(0m, (await _db.Assets().GetAsync(actor, asset.Id)).CurrentMeterValue);
    }
}
