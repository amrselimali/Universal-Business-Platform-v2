using Dapper;
using Npgsql;
using Ubp.Core.Models;
using Ubp.Core.Results;
using Xunit;

namespace Ubp.Tests;

/// <summary>
/// 8ج: خزينة رئيسية واحدة فقط على مستوى الشركة، وحسابات افتراضية لكل فرع على شجرة عامة (أ+ج).
/// </summary>
public sealed class BranchScopeFeatureTests : IClassFixture<TestDb>
{
    private readonly TestDb _db;

    public BranchScopeFeatureTests(TestDb db) => _db = db;

    private async Task<(Guid Branch, Guid TreasuryA, Guid TreasuryB)> SetupAsync(string suffix)
    {
        var actor = await _db.AdminActorAsync();
        var chart = await _db.SeedAccountingAsync();
        var branch = await _db.CreateBranchAsync("BF-B-" + suffix, "فرع الميزات " + suffix);

        var first = Guid.CreateVersion7();
        await _db.TreasuryRepo.CreateTreasuryAsync(first, new TreasuryInput
        {
            BranchId = branch, Code = "BF-T1-" + suffix, NameAr = "خزينة 1 " + suffix,
            TreasuryType = TreasuryTypes.Cash, AccountId = chart["1101"].Id
        }, actor.UserId);

        var second = Guid.CreateVersion7();
        await _db.TreasuryRepo.CreateTreasuryAsync(second, new TreasuryInput
        {
            BranchId = branch, Code = "BF-T2-" + suffix, NameAr = "خزينة 2 " + suffix,
            TreasuryType = TreasuryTypes.Cash, AccountId = chart["1101"].Id
        }, actor.UserId);

        return (branch, first, second);
    }

    [Fact]
    public async Task Only_one_main_treasury_can_exist()
    {
        var actor = await _db.AdminActorAsync();
        var (_, first, second) = await SetupAsync("1");
        var service = _db.TreasuryService();

        await service.SetMainTreasuryAsync(actor, first);
        await service.SetMainTreasuryAsync(actor, second);

        var treasuries = await _db.TreasuryRepo.ListTreasuriesAsync(null);
        Assert.Single(treasuries.Where(t => t.IsMain));
        Assert.True(treasuries.Single(t => t.IsMain).Id == second);
        Assert.Equal(second, await service.MainTreasuryIdAsync());
    }

    [Fact]
    public async Task An_inactive_treasury_is_not_selectable_as_main()
    {
        var actor = await _db.AdminActorAsync();
        var (_, first, second) = await SetupAsync("2");
        await using (var connection = new NpgsqlConnection(TestDb.AdminConnection))
        {
            await connection.OpenAsync();
            await connection.ExecuteAsync("UPDATE treasuries SET is_active = false WHERE id = @id",
                new { id = second });
        }
        var service = _db.TreasuryService();

        // الخزائن غير المفعّلة لا تظهر في القائمة، فلا يمكن اختيارها رئيسية
        await Assert.ThrowsAsync<NotFoundAppException>(async () =>
            await service.SetMainTreasuryAsync(actor, second));

        await service.SetMainTreasuryAsync(actor, first);
        Assert.Equal(first, await service.MainTreasuryIdAsync());
    }

    [Fact]
    public async Task Branch_default_accounts_are_saved_resolved_and_removed()
    {
        var actor = await _db.AdminActorAsync();
        await _db.SeedAccountingAsync();
        var branch = await _db.CreateBranchAsync("BF-B-DEF", "فرع الحسابات");
        var postable = (await _db.AccountRepo.ListPostableAsync()).First();
        var service = _db.BranchDefaults();

        await service.SetAsync(actor, new BranchAccountDefaultInput(branch, "Cash", postable.Id));

        var saved = Assert.Single(await service.ListAsync(actor, branch));
        Assert.Equal("cash", saved.Key);
        Assert.Equal(postable.Id, saved.AccountId);
        Assert.Equal(postable.Id, await service.ResolveAsync(branch, "cash"));

        // Upsert لنفس المفتاح لا ينشئ صفاً ثانياً
        await service.SetAsync(actor, new BranchAccountDefaultInput(branch, "cash", postable.Id));
        Assert.Single(await service.ListAsync(actor, branch));

        await service.RemoveAsync(actor, branch, "cash");
        Assert.Empty(await service.ListAsync(actor, branch));
    }

    [Fact]
    public async Task Invalid_keys_and_non_postable_accounts_are_rejected()
    {
        var actor = await _db.AdminActorAsync();
        var chart = await _db.SeedAccountingAsync();
        var branch = await _db.CreateBranchAsync("BF-B-BAD", "فرع الرفض");
        var service = _db.BranchDefaults();

        var badKey = await Assert.ThrowsAsync<ValidationAppException>(async () =>
            await service.SetAsync(actor, new BranchAccountDefaultInput(branch, "1-bad key!",
                chart["1101"].Id)));
        Assert.Equal("validation.branch_account_key_invalid", badKey.Code);

        // حساب رأس مجموعة (غير قابل للترحيل)
        var postableIds = (await _db.AccountRepo.ListPostableAsync()).Select(p => p.Id).ToHashSet();
        var parent = (await _db.AccountRepo.ListAllAsync(false))
            .First(a => !postableIds.Contains(a.Id));
        var notPostable = await Assert.ThrowsAsync<ValidationAppException>(async () =>
            await service.SetAsync(actor, new BranchAccountDefaultInput(branch, "inventory", parent.Id)));
        Assert.Equal("validation.account_not_postable", notPostable.Code);
    }
}
