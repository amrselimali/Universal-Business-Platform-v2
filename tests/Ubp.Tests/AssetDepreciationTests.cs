using Dapper;
using Npgsql;
using Ubp.Core.Models;
using Ubp.Core.Results;
using Xunit;

namespace Ubp.Tests;

/// <summary>
/// محرك الإهلاك (8أ): شهري أو سنوي حسب التصنيف، بقيد محاسبي (مصروف/مجمع)،
/// ومنع إهلاك نفس الشهر مرتين.
/// </summary>
public sealed class AssetDepreciationTests : IClassFixture<TestDb>
{
    private readonly TestDb _db;

    public AssetDepreciationTests(TestDb db) => _db = db;

    private async Task<Guid> CreateCategoryAsync(string frequency, int lifeMonths,
        Guid expenseAccount, Guid accumAccount)
    {
        await using var connection = new NpgsqlConnection(TestDb.AdminConnection);
        await connection.OpenAsync();
        return await connection.ExecuteScalarAsync<Guid>("""
            INSERT INTO asset_categories
                (code, name_ar, depreciation_method, depreciation_frequency, useful_life_months,
                 expense_account_id, accum_account_id)
            VALUES (@code, @name, 'LINEAR', @frequency, @life, @expense, @accum)
            RETURNING id
            """, new
        {
            code = "DP" + Guid.NewGuid().ToString("N")[..6], name = "تصنيف إهلاك",
            frequency, life = lifeMonths, expense = expenseAccount, accum = accumAccount
        });
    }

    [Fact]
    public async Task Monthly_linear_depreciation_posts_one_entry_and_records_the_line()
    {
        var actor = await _db.AdminActorAsync();
        var chart = await _db.SeedAccountingAsync();
        var postable = (await _db.AccountRepo.ListPostableAsync())
            .Where(a => !a.RequiresParty).Take(2).ToList();
        var branch = await _db.CreateBranchAsync("DP-B1", "فرع الإهلاك");
        var category = await CreateCategoryAsync(DepreciationFrequencies.Monthly, 10,
            postable[0].Id, postable[1].Id);

        var asset = await _db.Assets().CreateAsync(actor, new AssetInput
        {
            Code = "DP-A1", NameAr = "أصل يُهلَك", CategoryId = category, BranchId = branch,
            Cost = 1000, SalvageValue = 0, InServiceDate = DateTime.Today
        });

        var result = await _db.Assets().RunDepreciationAsync(actor, DateTime.Today, branch);

        Assert.Equal(100m, result.Total);
        Assert.Equal(1, result.AssetsDepreciated);
        Assert.NotNull(result.JournalEntryId);

        await using var connection = new NpgsqlConnection(TestDb.AdminConnection);
        await connection.OpenAsync();
        var line = await connection.QuerySingleAsync<(decimal Amount, decimal Accumulated)>("""
            SELECT amount, accumulated_after FROM asset_depreciation_lines WHERE asset_id = @id
            """, new { id = asset.Id });
        Assert.Equal(100m, line.Amount);
        Assert.Equal(100m, line.Accumulated);

        var entryStatus = await connection.ExecuteScalarAsync<string>(
            "SELECT status FROM journal_entries WHERE id = @id", new { id = result.JournalEntryId });
        Assert.Equal(EntryStatuses.Posted, entryStatus);
    }

    [Fact]
    public async Task Running_the_same_month_twice_is_rejected()
    {
        var actor = await _db.AdminActorAsync();
        var chart = await _db.SeedAccountingAsync();
        var postable = (await _db.AccountRepo.ListPostableAsync())
            .Where(a => !a.RequiresParty).Take(2).ToList();
        var branch = await _db.CreateBranchAsync("DP-B2", "فرع تكرار");
        var category = await CreateCategoryAsync(DepreciationFrequencies.Monthly, 12,
            postable[0].Id, postable[1].Id);
        await _db.Assets().CreateAsync(actor, new AssetInput
        {
            Code = "DP-A2", NameAr = "أصل", CategoryId = category, BranchId = branch, Cost = 1200
        });

        await _db.Assets().RunDepreciationAsync(actor, DateTime.Today, branch);
        await Assert.ThrowsAsync<ConflictException>(async () =>
            await _db.Assets().RunDepreciationAsync(actor, DateTime.Today, branch));
    }

    [Fact]
    public async Task Annual_category_only_depreciates_in_december()
    {
        var actor = await _db.AdminActorAsync();
        var chart = await _db.SeedAccountingAsync();
        var postable = (await _db.AccountRepo.ListPostableAsync())
            .Where(a => !a.RequiresParty).Take(2).ToList();
        var branch = await _db.CreateBranchAsync("DP-B3", "فرع سنوي");
        var category = await CreateCategoryAsync(DepreciationFrequencies.Annual, 12,
            postable[0].Id, postable[1].Id);
        await _db.Assets().CreateAsync(actor, new AssetInput
        {
            Code = "DP-A3", NameAr = "أصل سنوي", CategoryId = category, BranchId = branch,
            Cost = 1200, InServiceDate = new DateTime(2025, 1, 1)
        });

        var june = new DateTime(2025, 6, 1);
        await Assert.ThrowsAsync<ValidationAppException>(async () =>
            await _db.Assets().RunDepreciationAsync(actor, june, branch));

        var december = await _db.Assets().RunDepreciationAsync(actor,
            new DateTime(2025, 12, 1), branch);
        Assert.Equal(1200m, december.Total);
    }
}
