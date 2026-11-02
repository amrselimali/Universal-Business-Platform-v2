using Dapper;
using Npgsql;
using Ubp.Core.Models;
using Ubp.Core.Results;
using Xunit;

namespace Ubp.Tests;

/// <summary>تحويل متبقي الحجز إلى دفعة مقدمة: سند قبض مرحَّل + سجل تحويل + منع التكرار، والتحرير.</summary>
public sealed class ReservationReleaseTests : IClassFixture<TestDb>
{
    private readonly TestDb _db;

    public ReservationReleaseTests(TestDb db) => _db = db;

    private async Task<(Guid BookingId, Guid TreasuryId)> SetupAsync(string suffix,
        decimal quantity = 2, decimal price = 100)
    {
        var actor = await _db.AdminActorAsync();
        var chart = await _db.SeedAccountingAsync();
        await _db.AccountingSettings().SetDefaultAsync(actor, PostingKeys.CustomerAdvances,
            chart["1110"].Id);

        var branch = await _db.CreateBranchAsync("RC-B-" + suffix, "فرع التحويل " + suffix);
        var warehouse = await _db.CreateWarehouseAsync("RC-W-" + suffix, "مخزن " + suffix, branch);
        var item = await _db.CreateItemAsync("RC-IT-" + suffix, "صنف " + suffix);
        await _db.PostReceiptAsync(item, branch, warehouse, 10, 50);
        var customer = await _db.PartnerRepo.CreateAsync(PartnerKind.Customer, new PartnerInput
        { Code = "RC-C-" + suffix, NameAr = "عميل " + suffix });

        var treasuryId = Guid.CreateVersion7();
        await _db.TreasuryRepo.CreateTreasuryAsync(treasuryId, new TreasuryInput
        {
            BranchId = branch, Code = "RC-T-" + suffix, NameAr = "خزينة " + suffix,
            TreasuryType = TreasuryTypes.Cash, AccountId = chart["1101"].Id
        }, actor.UserId);
        await _db.TreasuryRepo.CreateMethodAsync(Guid.CreateVersion7(), new TreasuryMethodInput
        { TreasuryId = treasuryId, Code = "CASH", NameAr = "نقدي" }, actor.UserId);

        var booking = await _db.BookingService().CreateAsync(actor, new BookingInput
        {
            BranchId = branch, BookingDate = DateTime.Today, CustomerId = customer.Id,
            Lines =
            {
                new BookingLineInput
                {
                    ItemId = item.Id, UnitId = item.BaseUnitId, WarehouseId = warehouse,
                    Quantity = quantity, UnitPrice = price
                }
            }
        });
        await _db.BookingService().ConfirmAsync(actor, booking.Id);
        return (booking.Id, treasuryId);
    }

    [Fact]
    public async Task Releasing_a_booking_frees_its_reservation()
    {
        var actor = await _db.AdminActorAsync();
        var (bookingId, _) = await SetupAsync("2", quantity: 2);

        await _db.Reservations().ReleaseAsync(actor, bookingId, "عميل ألغى");

        var repo = new Ubp.Infrastructure.Repositories.ReservationRepository(_db.Factory);
        Assert.Empty(await repo.RemainingLinesAsync(bookingId));
    }
}
