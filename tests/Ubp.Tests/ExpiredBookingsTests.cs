using Dapper;
using Npgsql;
using Ubp.Core.Models;
using Xunit;

namespace Ubp.Tests;

public sealed class ExpiredBookingsTests : IClassFixture<TestDb>
{
    private readonly TestDb _db;

    public ExpiredBookingsTests(TestDb db) => _db = db;

    [Fact]
    public async Task Lists_expired_bookings_with_their_remaining_quantity_and_value()
    {
        var actor = await _db.AdminActorAsync();
        var branch = await _db.CreateBranchAsync("EXP-B-1", "فرع المنتهية");
        var warehouse = await _db.CreateWarehouseAsync("EXP-W-1", "مخزن المنتهية", branch);
        var item = await _db.CreateItemAsync("EXP-IT-1", "صنف المنتهية");
        await _db.PostReceiptAsync(item, branch, warehouse, 10, 50);
        var customer = await _db.PartnerRepo.CreateAsync(PartnerKind.Customer, new PartnerInput
        { Code = "EXP-C-1", NameAr = "عميل المنتهية" });

        var booking = await _db.BookingService().CreateAsync(actor, new BookingInput
        {
            BranchId = branch, BookingDate = DateTime.Today, CustomerId = customer.Id,
            ExpiresAt = DateTimeOffset.UtcNow.AddMonths(1),
            Lines =
            {
                new BookingLineInput
                {
                    ItemId = item.Id, UnitId = item.BaseUnitId, WarehouseId = warehouse,
                    Quantity = 4, UnitPrice = 0.5m
                }
            }
        });
        await _db.BookingService().ConfirmAsync(actor, booking.Id);

        await using (var connection = new NpgsqlConnection(TestDb.AdminConnection))
        {
            await connection.OpenAsync();
            await connection.ExecuteAsync(
                "UPDATE bookings SET expires_at = now() - interval '10 days' WHERE id = @id",
                new { id = booking.Id });
        }

        var rows = await _db.SalesFinance().ListExpiredBookingsAsync(actor, branch, DateTime.UtcNow);
        var row = Assert.Single(rows);
        Assert.Equal(booking.Id, row.BookingId);
        Assert.Equal(4m, row.RemainingQty);
        Assert.Equal(2m, row.RemainingValue);
        Assert.True(row.LateDays >= 9);
        Assert.Null(row.SettlementId);
    }
}
