using Dapper;
using Npgsql;
using Ubp.Core.Models;
using Xunit;

namespace Ubp.Tests;

/// <summary>صلاحية العرض: تُسجَّل على الحجز وتُمدَّد من شاشتي الحجوزات والدفعات المقدمة.</summary>
public sealed class BookingExpiryTests : IClassFixture<TestDb>
{
    private readonly TestDb _db;

    public BookingExpiryTests(TestDb db) => _db = db;

    [Fact]
    public async Task Expiry_is_saved_and_extended_from_both_screens()
    {
        var actor = await _db.AdminActorAsync();
        var branch = await _db.CreateBranchAsync("EXP2-B", "فرع الصلاحية");
        var warehouse = await _db.CreateWarehouseAsync("EXP2-W", "مخزن الصلاحية", branch);
        var item = await _db.CreateItemAsync("EXP2-IT", "صنف الصلاحية");
        var customer = await _db.PartnerRepo.CreateAsync(PartnerKind.Customer, new PartnerInput
        { Code = "EXP2-C", NameAr = "عميل الصلاحية" });

        var firstExpiry = new DateTimeOffset(DateTime.UtcNow.Date.AddMonths(1), TimeSpan.Zero);
        var booking = await _db.BookingService().CreateAsync(actor, new BookingInput
        {
            BranchId = branch, BookingDate = DateTime.Today, CustomerId = customer.Id,
            ExpiresAt = firstExpiry,
            Lines =
            {
                new BookingLineInput
                {
                    ItemId = item.Id, UnitId = item.BaseUnitId, WarehouseId = warehouse,
                    Quantity = 1, UnitPrice = 10
                }
            }
        });
        Assert.NotNull(booking.ExpiresAt);
        Assert.Equal(firstExpiry.UtcDateTime.Date, booking.ExpiresAt!.Value.UtcDateTime.Date);

        // تمديد من شاشة الحجوزات.
        var secondExpiry = new DateTimeOffset(DateTime.UtcNow.Date.AddMonths(2), TimeSpan.Zero);
        var extended = await _db.BookingService().ExtendExpiryAsync(actor, booking.Id, secondExpiry);
        Assert.Equal(secondExpiry.UtcDateTime.Date, extended.ExpiresAt!.Value.UtcDateTime.Date);

        // تمديد من شاشة الدفعات المقدمة.
        var thirdExpiry = new DateTimeOffset(DateTime.UtcNow.Date.AddMonths(3), TimeSpan.Zero);
        await _db.Reservations().ExtendAsync(actor, booking.Id, thirdExpiry);
        var reloaded = await _db.BookingService().GetAsync(actor, booking.Id);
        Assert.Equal(thirdExpiry.UtcDateTime.Date, reloaded.ExpiresAt!.Value.UtcDateTime.Date);

        await using var connection = new NpgsqlConnection(TestDb.AdminConnection);
        await connection.OpenAsync();
        Assert.Equal(2, await connection.ExecuteScalarAsync<int>(
            "SELECT count(*)::int FROM reservation_adjustments " +
            "WHERE booking_id = @id AND kind = 'EXTEND'", new { id = booking.Id }));
    }

    [Fact]
    public async Task Past_expiry_is_rejected_on_booking_creation()
    {
        var actor = await _db.AdminActorAsync();
        var branch = await _db.CreateBranchAsync("EXP3-B", "فرع صلاحية مرفوضة");
        var warehouse = await _db.CreateWarehouseAsync("EXP3-W", "مخزن صلاحية مرفوضة", branch);
        var item = await _db.CreateItemAsync("EXP3-IT", "صنف صلاحية مرفوضة");
        var customer = await _db.PartnerRepo.CreateAsync(PartnerKind.Customer, new PartnerInput
        { Code = "EXP3-C", NameAr = "عميل صلاحية مرفوضة" });

        var error = await Assert.ThrowsAsync<Ubp.Core.Results.ValidationAppException>(() =>
            _db.BookingService().CreateAsync(actor, new BookingInput
            {
                BranchId = branch, BookingDate = DateTime.Today, CustomerId = customer.Id,
                ExpiresAt = DateTimeOffset.UtcNow.AddDays(-1),
                Lines =
                {
                    new BookingLineInput
                    {
                        ItemId = item.Id, UnitId = item.BaseUnitId, WarehouseId = warehouse,
                        Quantity = 1, UnitPrice = 10
                    }
                }
            }));
        Assert.Equal("validation.booking_expiry_invalid", error.Code);
    }
}
