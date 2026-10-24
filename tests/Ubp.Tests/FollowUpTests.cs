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
}
