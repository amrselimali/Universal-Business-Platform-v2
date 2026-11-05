using Ubp.Core.Models;
using Ubp.Core.Paging;
using Xunit;

namespace Ubp.Tests;

/// <summary>
/// فلاتر شاشة حجز المواعيد: بحث باسم/كود/تليفون العميل، حالات متعددة (مسودة/مؤكد/ملغى)،
/// ونطاق تاريخ من/إلى — مع تطبيق نطاق فروع المستخدم على النتائج.
/// </summary>
public sealed class BookingFilterTests : IClassFixture<TestDb>
{
    private readonly TestDb _db;

    public BookingFilterTests(TestDb db) => _db = db;

    private async Task<(Guid Branch, Guid Warehouse, Item Item, Partner Customer)> SetupAsync(
        string suffix)
    {
        await _db.SeedAccountingAsync();
        var branch = await _db.CreateBranchAsync("BF-B-" + suffix, "فرع فلتر " + suffix);
        var warehouse = await _db.CreateWarehouseAsync("BF-W-" + suffix, "مخزن " + suffix, branch);
        var item = await _db.CreateItemAsync("BF-IT-" + suffix, "صنف " + suffix);
        await _db.PostReceiptAsync(item, branch, warehouse, 50, 10);
        var customer = await _db.PartnerRepo.CreateAsync(PartnerKind.Customer, new PartnerInput
        {
            Code = "BF-C-" + suffix, NameAr = "عميل فلتر " + suffix, Phone = "0100000" + suffix
        });
        return (branch, warehouse, item, customer);
    }

    private async Task<Booking> CreateAsync(Guid branch, Guid warehouse, Partner customer,
        Item item, DateTime date) =>
        await _db.BookingService().CreateAsync(await _db.AdminActorAsync(), new BookingInput
        {
            BranchId = branch, BookingDate = date, CustomerId = customer.Id,
            Lines =
            {
                new BookingLineInput
                {
                    ItemId = item.Id, UnitId = item.BaseUnitId, WarehouseId = warehouse,
                    Quantity = 1, UnitPrice = 10
                }
            }
        });

    [Fact]
    public async Task Default_filter_returns_draft_and_confirmed_today_only()
    {
        var actor = await _db.AdminActorAsync();
        var service = _db.BookingService();
        var (branch, warehouse, item, customer) = await SetupAsync("1");

        var draft = await CreateAsync(branch, warehouse, customer, item, DateTime.Today);
        var confirmed = await CreateAsync(branch, warehouse, customer, item, DateTime.Today);
        await service.ConfirmAsync(actor, confirmed.Id);
        var cancelled = await CreateAsync(branch, warehouse, customer, item, DateTime.Today);
        await service.CancelAsync(actor, cancelled.Id, "اختبار");
        var tomorrow = await CreateAsync(branch, warehouse, customer, item, DateTime.Today.AddDays(1));

        var page = await service.ListAsync(actor, new BookingFilter
        {
            BranchId = branch,
            Statuses = new List<string> { BookingStatuses.Draft, BookingStatuses.Confirmed },
            From = DateTime.Today, To = DateTime.Today
        }, new PageRequest { Page = 1, PageSize = 50 });

        Assert.Equal(2, page.Total);
        Assert.Contains(page.Items, b => b.Id == draft.Id);
        Assert.Contains(page.Items, b => b.Id == confirmed.Id);
        Assert.DoesNotContain(page.Items, b => b.Id == cancelled.Id);
        Assert.DoesNotContain(page.Items, b => b.Id == tomorrow.Id);
    }

    [Fact]
    public async Task Cancelled_only_and_date_range_filters_work()
    {
        var actor = await _db.AdminActorAsync();
        var service = _db.BookingService();
        var (branch, warehouse, item, customer) = await SetupAsync("2");

        var cancelled = await CreateAsync(branch, warehouse, customer, item, DateTime.Today.AddDays(-2));
        await service.CancelAsync(actor, cancelled.Id, "اختبار");
        await CreateAsync(branch, warehouse, customer, item, DateTime.Today);

        var page = await service.ListAsync(actor, new BookingFilter
        {
            BranchId = branch,
            Statuses = new List<string> { BookingStatuses.Cancelled },
            From = DateTime.Today.AddDays(-3), To = DateTime.Today
        }, new PageRequest { Page = 1, PageSize = 50 });

        var row = Assert.Single(page.Items);
        Assert.Equal(cancelled.Id, row.Id);
    }

    [Fact]
    public async Task Search_matches_customer_code_and_phone()
    {
        var actor = await _db.AdminActorAsync();
        var service = _db.BookingService();
        var (branch, warehouse, item, customer) = await SetupAsync("3");
        await CreateAsync(branch, warehouse, customer, item, DateTime.Today);

        foreach (var term in new[] { "BF-C-3", "01000003", "عميل فلتر 3" })
        {
            var page = await service.ListAsync(actor, new BookingFilter
            {
                BranchId = branch, Search = term,
                Statuses = new List<string> { BookingStatuses.Draft },
                From = DateTime.Today, To = DateTime.Today
            }, new PageRequest { Page = 1, PageSize = 50 });
            Assert.True(page.Total >= 1, "no match for " + term);
        }
    }
}
