using Dapper;
using Npgsql;
using Ubp.Core.Models;
using Xunit;

namespace Ubp.Tests;

/// <summary>تحويل الخدمات بين العملاء: مسودة بلا أثر، وعند الترحيل نقل الكميات والقيد 0083.</summary>
public sealed class ServiceTransferTests : IClassFixture<TestDb>
{
    private readonly TestDb _db;

    public ServiceTransferTests(TestDb db) => _db = db;

    private sealed record Fixture(Guid Branch, Guid Warehouse, Item Service, Partner From,
        Partner To, Guid BookingLineId, Guid ShiftId);

    private async Task<Fixture> SetupAsync(string suffix)
    {
        var actor = await _db.AdminActorAsync();
        await _db.SeedAccountingAsync();
        var branch = await _db.CreateBranchAsync("TR-B-" + suffix, "فرع تحويل " + suffix);
        var warehouse = await _db.CreateWarehouseAsync("TR-W-" + suffix, "مخزن تحويل " + suffix, branch);
        var unit = await _db.UnitRepo.CreateAsync(new UnitInput
        { Code = "TR-U-" + suffix, NameAr = "وحدة تحويل " + suffix });
        var service = await _db.ItemRepo.CreateAsync(new ItemInput
        {
            Code = "TR-SV-" + suffix, NameAr = "خدمة تحويل " + suffix, ItemType = "SERVICE",
            BaseUnitId = unit.Id,
            Units = new List<ItemUnitInput> { new() { UnitId = unit.Id, ConversionFactor = 1 } }
        });
        var from = await _db.PartnerRepo.CreateAsync(PartnerKind.Customer, new PartnerInput
        { Code = "TR-C1-" + suffix, NameAr = "عميل مصدر " + suffix });
        var to = await _db.PartnerRepo.CreateAsync(PartnerKind.Customer, new PartnerInput
        { Code = "TR-C2-" + suffix, NameAr = "عميل هدف " + suffix });

        var booking = await _db.BookingService().CreateAsync(actor, new BookingInput
        {
            BranchId = branch, BookingDate = DateTime.Today, CustomerId = from.Id,
            Lines =
            {
                new BookingLineInput
                {
                    ItemId = service.Id, UnitId = service.BaseUnitId, WarehouseId = warehouse,
                    Quantity = 5, UnitPrice = 100
                }
            }
        });
        await _db.BookingRepo.ConfirmAsync(booking.Id, actor.UserId, reserveStock: false);
        var line = (await _db.BookingService().GetAsync(actor, booking.Id)).Lines.Single();

        var shift = await _db.TreasuryService()
            .OpenShiftAsync(actor, new ShiftInput { BranchId = branch });

        return new Fixture(branch, warehouse, service, from, to, line.Id, shift.Id);
    }

    private static async Task<decimal> ReservedQtyAsync(Guid bookingLineId)
    {
        await using var connection = new NpgsqlConnection(TestDb.AdminConnection);
        await connection.OpenAsync();
        return await connection.ExecuteScalarAsync<decimal>(
            "SELECT reserved_qty FROM booking_lines WHERE id = @id", new { id = bookingLineId });
    }

    private static async Task<decimal> PartyBalanceAsync(Guid customerId)
    {
        await using var connection = new NpgsqlConnection(TestDb.AdminConnection);
        await connection.OpenAsync();
        return await connection.ExecuteScalarAsync<decimal>(
            "SELECT COALESCE(SUM(debit - credit), 0) FROM party_balances " +
            "WHERE party_type = 'C' AND party_id = @id", new { id = customerId });
    }

    [Fact]
    public async Task Draft_transfer_has_no_effect_then_posting_moves_quantity_and_posts_entry()
    {
        var f = await SetupAsync("1");
        var actor = await _db.AdminActorAsync();

        var available = await _db.ServiceTransferRepo.ListAvailableAsync(f.Branch, f.From.Id);
        Assert.Single(available);
        Assert.Equal(5m, available[0].RemainingQty);
        Assert.Equal(100m, available[0].UnitPrice);

        var draft = await _db.ServiceTransferRepo.CreateAsync(new ServiceTransferInput
        {
            BranchId = f.Branch, FromCustomerId = f.From.Id, ToCustomerId = f.To.Id,
            DocDate = DateTime.Today,
            Lines = { new ServiceTransferLineInput { FromBookingLineId = f.BookingLineId, Quantity = 2 } }
        }, actor.UserId);

        Assert.Equal(ServiceTransferStatuses.Draft, draft.Status);
        Assert.Equal(200m, draft.TotalAmount);
        // المسودة لا تُغيّر رصيد الكمية ولا القيد.
        Assert.Equal(5m, await ReservedQtyAsync(f.BookingLineId));
        Assert.Equal(0m, await PartyBalanceAsync(f.To.Id));

        var posted = await _db.ServiceTransferRepo.PostAsync(draft.Id, actor.UserId);
        Assert.Equal(ServiceTransferStatuses.Posted, posted.Status);

        // خصم الكمية من المصدر وإضافتها للهدف.
        Assert.Equal(3m, await ReservedQtyAsync(f.BookingLineId));
        var targetAvailable = await _db.ServiceTransferRepo.ListAvailableAsync(f.Branch, f.To.Id);
        Assert.Single(targetAvailable);
        Assert.Equal(2m, targetAvailable[0].RemainingQty);

        // القيد: مدين حساب العميل الهدف، دائن حساب العميل المصدر بنفس القيمة.
        Assert.Equal(200m, await PartyBalanceAsync(f.To.Id));
        Assert.Equal(-200m, await PartyBalanceAsync(f.From.Id));
    }

    [Fact]
    public async Task Posted_transfer_appears_in_the_shift_report_revenues()
    {
        var f = await SetupAsync("2");
        var actor = await _db.AdminActorAsync();

        var draft = await _db.ServiceTransferRepo.CreateAsync(new ServiceTransferInput
        {
            BranchId = f.Branch, FromCustomerId = f.From.Id, ToCustomerId = f.To.Id,
            DocDate = DateTime.Today,
            Lines = { new ServiceTransferLineInput { FromBookingLineId = f.BookingLineId, Quantity = 1 } }
        }, actor.UserId);
        await _db.ServiceTransferRepo.PostAsync(draft.Id, actor.UserId);

        var report = await _db.TreasuryService().GetShiftReportAsync(actor, f.ShiftId);
        Assert.Contains(report.Lines, l => l.ItemName == f.Service.NameAr && l.Quantity == 1m);
    }
}
