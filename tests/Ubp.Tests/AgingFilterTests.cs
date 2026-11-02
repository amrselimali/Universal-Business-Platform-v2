using Ubp.Core.Models;
using Xunit;

namespace Ubp.Tests;

public sealed class AgingFilterTests : IClassFixture<TestDb>
{
    private readonly TestDb _db;

    public AgingFilterTests(TestDb db) => _db = db;

    [Fact]
    public async Task Aging_searches_customer_by_code_name_or_phone()
    {
        var actor = await _db.AdminActorAsync();
        var branch = await _db.CreateBranchAsync("AF-B-1", "فرع الأعمار");
        var warehouse = await _db.CreateWarehouseAsync("AF-W-1", "مخزن الأعمار", branch);
        var item = await _db.CreateItemAsync("AF-IT-1", "صنف الأعمار");
        await _db.PostReceiptAsync(item, branch, warehouse, 10, 50);
        var customer = await _db.PartnerRepo.CreateAsync(PartnerKind.Customer, new PartnerInput
        {
            Code = "AF-C-1", NameAr = "عميل الأعمار", Phone = "01198765432"
        });

        var booking = await _db.BookingService().CreateAsync(actor, new BookingInput
        {
            BranchId = branch, CustomerId = customer.Id, BookingDate = DateTime.Today.AddDays(-5),
            Lines = { new BookingLineInput { ItemId = item.Id, UnitId = item.BaseUnitId,
                WarehouseId = warehouse, Quantity = 3, UnitPrice = 100 } }
        });
        await _db.BookingService().ConfirmAsync(actor, booking.Id);

        var byCode = await _db.SalesFinance().AgingQuantitiesAsync(actor, branch,
            DateTime.Today, "AF-C-1");
        Assert.Contains(byCode, x => x.BookingId == booking.Id);

        var byName = await _db.SalesFinance().AgingQuantitiesAsync(actor, branch,
            DateTime.Today, "عميل الأعمار");
        Assert.Contains(byName, x => x.BookingId == booking.Id);

        var byPhone = await _db.SalesFinance().AgingQuantitiesAsync(actor, branch,
            DateTime.Today, "011987");
        Assert.Contains(byPhone, x => x.BookingId == booking.Id);

        var byBooking = await _db.SalesFinance().AgingAsync(actor, "bookings", branch,
            DateTime.Today, "AF-C-1");
        Assert.Equal(customer.NameAr, Assert.Single(byBooking).Reference);

        var none = await _db.SalesFinance().AgingAsync(actor, "bookings", branch,
            DateTime.Today, "NO-SUCH-CUSTOMER");
        Assert.Empty(none);
    }
}
