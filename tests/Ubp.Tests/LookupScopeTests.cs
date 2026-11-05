using Ubp.Application.Services;
using Ubp.Core.Models;
using Ubp.Core.Security;
using Xunit;

namespace Ubp.Tests;

/// <summary>
/// ربط المستخدم بالشاشات حسب الفرع والمخزن المصرح به: قوائم الاختيار (أصناف/مخازن/
/// خزائن/طرق دفع/حسابات/مراكز تكلفة) لا تطلب صلاحية شاشة أخرى، وتُقيَّد بالمصرح فقط.
/// </summary>
public sealed class LookupScopeTests : IClassFixture<TestDb>
{
    private readonly TestDb _db;
    private readonly LookupService _lookup;

    public LookupScopeTests(TestDb db)
    {
        _db = db;
        _lookup = new LookupService(db.PartnerRepo, db.ItemRepo, db.WarehouseRepo,
            db.ItemCategoryRepo, db.TreasuryRepo, db.AccountRepo, db.CostCenterRepo);
    }

    private static ActorContext ReceptionActor(Guid branch, Guid[]? warehouses = null) => new()
    {
        UserId = Guid.NewGuid(), Username = "reception", IsAdmin = false,
        CurrentBranchId = branch, AllowedBranchIds = new[] { branch }, TraceId = "test"
    };

    [Fact]
    public async Task Warehouses_are_limited_to_the_user_assigned_warehouse()
    {
        await _db.SeedAccountingAsync();
        var branch = await _db.CreateBranchAsync("LK-B-1", "فرع استقبال 1");
        var linked = await _db.CreateWarehouseAsync("LK-W-1", "مخزن مربوط", branch);
        await _db.CreateWarehouseAsync("LK-W-2", "مخزن غير مربوط", branch);

        var userId = await _db.UserRepo.CreateAsync(new UserInput
        {
            Username = "recv-" + Guid.NewGuid().ToString("N")[..8], Name = "استقبال",
            Password = "P@ssw0rd!2026", IsActive = true, BranchIds = new List<Guid> { branch },
            DefaultBranchId = branch, WarehouseIds = new List<Guid> { linked }
        }, "hash");

        // لا يملك أي صلاحية شاشات، ومع ذلك يرى مخزنه المصرح به فقط.
        var actor = new ActorContext
        {
            UserId = userId, Username = "recv", IsAdmin = false, CurrentBranchId = branch,
            AllowedBranchIds = new[] { branch }, TraceId = "test"
        };

        var warehouses = await _lookup.WarehousesAsync(actor);
        Assert.Contains(warehouses, w => w.Id == linked);
        Assert.DoesNotContain(warehouses, w => w.Code == "LK-W-2");
    }

    [Fact]
    public async Task Items_treasuries_and_methods_are_available_without_other_screen_permissions()
    {
        var admin = await _db.AdminActorAsync();
        var chart = await _db.SeedAccountingAsync();
        var branch = await _db.CreateBranchAsync("LK-B-2", "فرع استقبال 2");
        var item = await _db.CreateItemAsync("LK-IT-1", "خدمة استقبال");

        var treasuryId = Guid.CreateVersion7();
        await _db.TreasuryRepo.CreateTreasuryAsync(treasuryId, new TreasuryInput
        {
            BranchId = branch, Code = "LK-T-1", NameAr = "خزينة استقبال",
            TreasuryType = TreasuryTypes.Cash, AccountId = chart["1101"].Id
        }, admin.UserId);
        await _db.TreasuryRepo.CreateMethodAsync(Guid.CreateVersion7(), new TreasuryMethodInput
        {
            TreasuryId = treasuryId, Code = "CASH", NameAr = "نقدي",
            MethodType = PaymentMethodTypes.Cash
        }, admin.UserId);

        var actor = ReceptionActor(branch);

        var items = await _lookup.ItemsAsync(actor, "LK-IT-1", 50, true);
        Assert.Contains(items, i => i.Id == item.Id);

        var treasuries = await _lookup.TreasuriesAsync(actor, branch);
        Assert.Contains(treasuries, t => t.Id == treasuryId);

        var methods = await _lookup.TreasuryMethodsAsync(actor, branch);
        Assert.Contains(methods, m => m.TreasuryId == treasuryId);

        var accounts = await _lookup.PostableAccountsAsync(actor);
        Assert.NotEmpty(accounts);
    }
}
