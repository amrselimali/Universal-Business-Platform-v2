using Ubp.Core.Models;
using Xunit;

namespace Ubp.Tests;

public sealed class FollowUpTests : IClassFixture<TestDb>
{
    private readonly TestDb _db;
    public FollowUpTests(TestDb db) => _db = db;

    private async Task<(Guid Branch, Guid Warehouse, Item Item, Partner Customer)> SetupAsync(
        string suffix)
    {
        var actor = await _db.AdminActorAsync();
        await _db.SeedAccountingAsync();
        var branch = await _db.CreateBranchAsync("FU-B-" + suffix, "فرع متابعة " + suffix);
        var warehouse = await _db.CreateWarehouseAsync("FU-W-" + suffix, "مخزن " + suffix, branch);
        var item = await _db.CreateItemAsync("FU-IT-" + suffix, "صنف " + suffix);
        await _db.PostReceiptAsync(item, branch, warehouse, 5, 10);
        var customer = await _db.PartnerRepo.CreateAsync(PartnerKind.Customer, new PartnerInput
        { Code = "FU-C-" + suffix, NameAr = "عميل " + suffix });
        return (branch, warehouse, item, customer);
    }

    [Fact]
    public async Task Followup_is_created_and_completed()
    {
        var actor = await _db.AdminActorAsync();
        var (_, _, _, customer) = await SetupAsync("1");

        var followUp = await _db.FollowUps().CreateAsync(actor, new FollowUpInput
        {
            CustomerId = customer.Id, Channel = FollowUpChannels.WhatsApp,
            DueAt = DateTimeOffset.Now.AddHours(2), Note = "تذكير"
        });
        Assert.True(followUp.IsPending);

        var list = await _db.FollowUps().ListAsync(actor, FollowUpStatuses.Pending);
        Assert.Contains(list, f => f.Id == followUp.Id);

        var done = await _db.FollowUps().CompleteAsync(actor, followUp.Id, followUp.Version);
        Assert.Equal(FollowUpStatuses.Done, done.Status);
        Assert.NotNull(done.CompletedAt);
    }

    [Fact]
    public async Task Overdue_bookings_generate_followups_once()
    {
        var actor = await _db.AdminActorAsync();
        var (branch, warehouse, item, customer) = await SetupAsync("2");

        var booking = await _db.BookingService().CreateAsync(actor, new BookingInput
        {
            BranchId = branch, CustomerId = customer.Id,
            BookingDate = DateTime.Today.AddDays(-10), DueDate = DateTime.Today.AddDays(-5),
            Lines = { new BookingLineInput { ItemId = item.Id, UnitId = item.BaseUnitId,
                WarehouseId = warehouse, Quantity = 1, UnitPrice = 10 } }
        });
        await _db.BookingService().ConfirmAsync(actor, booking.Id);

        var first = await _db.FollowUps().CreateOverdueAsync(actor);
        Assert.Equal(1, first);
        var second = await _db.FollowUps().CreateOverdueAsync(actor);
        Assert.Equal(0, second);
    }

    [Fact]
    public async Task Idle_search_lists_customers_without_activity_and_reports_the_last_one()
    {
        var actor = await _db.AdminActorAsync();
        var (_, _, _, customer) = await SetupAsync("3");

        // نشاط قديم خارج الفترة: العميل يظهر ومعه آخر متابعة وملاحظتها.
        var past = await _db.FollowUps().CreateAsync(actor, new FollowUpInput
        {
            CustomerId = customer.Id, Channel = FollowUpChannels.Call,
            DueAt = DateTimeOffset.Now.AddDays(-3), Note = "متابعة قديمة"
        });
        await _db.FollowUps().CompleteAsync(actor, past.Id, past.Version);

        var rows = await _db.FollowUps().FindIdleCustomersAsync(actor, customer.Code,
            customer.Code, null);
        var row = Assert.Single(rows.Where(r => r.CustomerId == customer.Id));
        Assert.NotNull(row.LastFollowUpAt);
        Assert.Equal("متابعة قديمة", row.LastFollowUpNote);
        Assert.Equal(FollowUpStatuses.Done, row.LastFollowUpStatus);

        // متابعة اليوم تستبعده من نتيجة البحث.
        await _db.FollowUps().CreateAsync(actor, new FollowUpInput
        {
            CustomerId = customer.Id, Channel = FollowUpChannels.WhatsApp,
            DueAt = DateTimeOffset.Now.AddHours(2), Note = "اليوم"
        });
        var after = await _db.FollowUps().FindIdleCustomersAsync(actor, customer.Code,
            customer.Code, null);
        Assert.DoesNotContain(after, r => r.CustomerId == customer.Id);
    }

    [Fact]
    public async Task Idle_search_excludes_customers_with_a_booking_inside_the_range()
    {
        var actor = await _db.AdminActorAsync();
        var (branch, warehouse, item, customer) = await SetupAsync("4");

        var booking = await _db.BookingService().CreateAsync(actor, new BookingInput
        {
            BranchId = branch, CustomerId = customer.Id, BookingDate = DateTime.Today,
            Lines = { new BookingLineInput { ItemId = item.Id, UnitId = item.BaseUnitId,
                WarehouseId = warehouse, Quantity = 1, UnitPrice = 10 } }
        });
        await _db.BookingService().ConfirmAsync(actor, booking.Id);

        var rows = await _db.FollowUps().FindIdleCustomersAsync(actor, customer.Code,
            customer.Code, null);
        Assert.DoesNotContain(rows, r => r.CustomerId == customer.Id);
    }

    [Fact]
    public async Task Idle_search_ignores_old_activity_and_filters_by_the_code_range()
    {
        var actor = await _db.AdminActorAsync();
        var (branch, warehouse, item, customer) = await SetupAsync("6");

        // حجز قديم (قبل اليوم) لا يمنع ظهور العميل في نتيجة «بلا نشاط مستقبلي».
        var pastBooking = await _db.BookingService().CreateAsync(actor, new BookingInput
        {
            BranchId = branch, CustomerId = customer.Id, BookingDate = DateTime.Today.AddDays(-40),
            Lines = { new BookingLineInput { ItemId = item.Id, UnitId = item.BaseUnitId,
                WarehouseId = warehouse, Quantity = 1, UnitPrice = 10 } }
        });
        Assert.NotNull(pastBooking);

        var rows = await _db.FollowUps().FindIdleCustomersAsync(actor, customer.Code,
            customer.Code, null);
        var row = Assert.Single(rows.Where(r => r.CustomerId == customer.Id));
        Assert.NotNull(row.LastBookingAt);

        // خارج نطاق الأكواد لا يظهر.
        var outside = await _db.FollowUps().FindIdleCustomersAsync(actor, "ZZZ-9999", "ZZZ-9999",
            null);
        Assert.DoesNotContain(outside, r => r.CustomerId == customer.Id);
    }

    [Fact]
    public async Task Followup_list_filters_by_the_due_date_range()
    {
        var actor = await _db.AdminActorAsync();
        var (_, _, _, customer) = await SetupAsync("5");

        var today = await _db.FollowUps().CreateAsync(actor, new FollowUpInput
        { CustomerId = customer.Id, DueAt = DateTimeOffset.Now.AddHours(1), Note = "today" });
        var later = await _db.FollowUps().CreateAsync(actor, new FollowUpInput
        { CustomerId = customer.Id, DueAt = DateTimeOffset.Now.AddDays(10), Note = "later" });

        var rows = await _db.FollowUps().ListAsync(actor, null, DateTime.Today, DateTime.Today, null);
        Assert.Contains(rows, f => f.Id == today.Id);
        Assert.DoesNotContain(rows, f => f.Id == later.Id);
        Assert.Contains(rows, f => f.Id == today.Id && f.Note == "today");
    }
}
