using Ubp.Application.Services;
using Ubp.Core.Models;
using Ubp.Core.Results;
using Xunit;

namespace Ubp.Tests;

/// <summary>
/// مركز التقارير (الترحيل 0039): الفلاتر المحفوظة مقيّدة بالمستخدم وبقيد فريد
/// (user_id, report_key, filter_name)، والتقارير تظهر حسب صلاحية شاشتها.
/// </summary>
public sealed class ReportCenterTests : IClassFixture<TestDb>
{
    private readonly TestDb _db;

    public ReportCenterTests(TestDb db) => _db = db;

    private ReportCenterService Service() =>
        new(new Ubp.Infrastructure.Repositories.SavedReportFilterRepository(_db.Factory),
            _db.Permissions(),
            new StockReportService(_db.StockReportRepo, _db.ItemRepo, _db.WarehouseRepo,
                _db.Permissions()));

    [Fact]
    public async Task Admin_sees_every_approved_report()
    {
        var actor = await _db.AdminActorAsync();
        var reports = await Service().ReportsAsync(actor);
        Assert.Equal(ReportCatalog.All.Count, reports.Count);
        Assert.Contains(reports, r => r.Key == "stock.balances");
    }

    [Fact]
    public async Task Saving_the_same_name_twice_updates_the_same_filter()
    {
        var actor = await _db.AdminActorAsync();
        var service = Service();

        var first = await service.SaveFilterAsync(actor, "stock.balances", "الفرع الرئيسي",
            new Dictionary<string, string?> { ["warehouse_id"] = Guid.NewGuid().ToString() });
        var second = await service.SaveFilterAsync(actor, "stock.balances", "الفرع الرئيسي",
            new Dictionary<string, string?> { ["include_zero"] = "true" });

        Assert.Equal(first.Id, second.Id);
        Assert.Contains("include_zero", second.FiltersJson);
        var sameName = (await service.FiltersAsync(actor, "stock.balances"))
            .Where(f => f.FilterName == "الفرع الرئيسي").ToList();
        Assert.Single(sameName);
    }

    [Fact]
    public async Task Filters_belong_to_their_owner_only()
    {
        var actor = await _db.AdminActorAsync();
        var service = Service();
        await service.SaveFilterAsync(actor, "stock.balances", "فلتر المالك",
            new Dictionary<string, string?> { ["include_zero"] = "true" });

        var repository = new Ubp.Infrastructure.Repositories.SavedReportFilterRepository(_db.Factory);
        var otherUser = await _db.CreateUserAsync("filters.other", "Str0ng!Other2026",
            [await _db.CreateBranchAsync("RC-O", "فرع مالك آخر")]);

        Assert.Empty(await repository.ListAsync(otherUser, "stock.balances"));
    }

    [Fact]
    public async Task Empty_name_and_unknown_report_are_rejected()
    {
        var actor = await _db.AdminActorAsync();
        var service = Service();

        var empty = await Assert.ThrowsAsync<ValidationAppException>(async () =>
            await service.SaveFilterAsync(actor, "stock.balances", "  ",
                new Dictionary<string, string?>()));
        Assert.Equal("validation.filter_name_required", empty.Code);

        var unknown = await Assert.ThrowsAsync<ValidationAppException>(async () =>
            await service.SaveFilterAsync(actor, "reports.unknown", "اسم",
                new Dictionary<string, string?>()));
        Assert.Equal("validation.report_key_unknown", unknown.Code);
    }

    [Fact]
    public async Task Deleting_a_filter_that_is_not_owned_returns_not_found()
    {
        var actor = await _db.AdminActorAsync();
        var service = Service();
        var filter = await service.SaveFilterAsync(actor, "stock.balances", "للحذف",
            new Dictionary<string, string?> { ["include_zero"] = "false" });

        await service.DeleteFilterAsync(actor, filter.Id);
        Assert.DoesNotContain((await service.FiltersAsync(actor, "stock.balances")),
            f => f.Id == filter.Id);

        await Assert.ThrowsAsync<NotFoundAppException>(async () =>
            await service.DeleteFilterAsync(actor, filter.Id));
    }
}
