using Ubp.Core.Paging;
using Xunit;

namespace Ubp.Tests;

public sealed class BranchIsolationTests : IClassFixture<TestDb>
{
    private readonly TestDb _db;

    public BranchIsolationTests(TestDb db) => _db = db;

    [Fact]
    public async Task Branch_list_only_returns_branches_assigned_to_the_user()
    {
        var branchA = await _db.CreateBranchAsync("ISO-A", "فرع أ");
        var branchB = await _db.CreateBranchAsync("ISO-B", "فرع ب");
        var userId = await _db.CreateUserAsync("iso.user", "Str0ng!Pass", new[] { branchA });

        var result = await _db.BranchRepo.ListAsync(new PageRequest { PageSize = 25 },
            scope: 1, actorId: userId, branchIds: new[] { branchA }, isAdmin: false);

        Assert.Single(result.Items);
        Assert.Equal(branchA, result.Items[0].Id);
        Assert.DoesNotContain(result.Items, b => b.Id == branchB);
    }

    [Fact]
    public async Task Warehouse_list_respects_branch_scope_and_user_links()
    {
        var branchA = await _db.CreateBranchAsync("WHISO-A", "فرع مخزن أ");
        var branchB = await _db.CreateBranchAsync("WHISO-B", "فرع مخزن ب");
        var warehouseA = await _db.CreateWarehouseAsync("WH-A", "مخزن أ", branchA);
        var warehouseB = await _db.CreateWarehouseAsync("WH-B", "مخزن ب", branchB);
        var userId = await _db.CreateUserAsync("wh.user", "Str0ng!Pass",
            new[] { branchA }, new[] { warehouseA });

        var branchScope = await _db.WarehouseRepo.ListAsync(new PageRequest { PageSize = 25 },
            scope: 1, actorId: userId, branchIds: new[] { branchA }, isAdmin: false);
        Assert.Contains(branchScope.Items, w => w.Id == warehouseA);
        Assert.DoesNotContain(branchScope.Items, w => w.Id == warehouseB);

        var ownScope = await _db.WarehouseRepo.ListAsync(new PageRequest { PageSize = 25 },
            scope: 0, actorId: userId, branchIds: new[] { branchA }, isAdmin: false);
        Assert.Single(ownScope.Items);
        Assert.Equal(warehouseA, ownScope.Items[0].Id);
    }

    [Fact]
    public async Task Admin_sees_all_branches()
    {
        await _db.CreateBranchAsync("ADMIN-A", "فرع أدمن أ");
        await _db.CreateBranchAsync("ADMIN-B", "فرع أدمن ب");
        var result = await _db.BranchRepo.ListAsync(new PageRequest { PageSize = 25 },
            scope: 2, actorId: Guid.NewGuid(), branchIds: Array.Empty<Guid>(), isAdmin: true);
        Assert.Contains(result.Items, b => b.Code == "ADMIN-A");
        Assert.Contains(result.Items, b => b.Code == "ADMIN-B");
    }
}
