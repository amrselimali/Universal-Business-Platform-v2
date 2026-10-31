using Dapper;
using Npgsql;
using Ubp.Core.Models;
using Ubp.Core.Paging;
using Ubp.Core.Results;
using Xunit;

namespace Ubp.Tests;

/// <summary>
/// 8ب: الحجوزات كدفعات مقدمة — الكميات المحجوزة/المستهلكة/المتبقية، وتحويل المتبقي إلى دفعة مقدمة
/// (سند قبض مرحَّل) مرة واحدة فقط، والرصيد البيعي بعد خصم المحجوز، ونطاق الفرع.
/// </summary>
public sealed class ReservationsTests : IClassFixture<TestDb>
{
    private readonly TestDb _db;

    public ReservationsTests(TestDb db) => _db = db;

    private Ubp.Infrastructure.Repositories.ReservationRepository Repo() =>
        new(_db.Factory);

    private sealed record Fixture(Guid Branch, Guid Warehouse, Item Item, Partner Customer,
        Booking Booking);

    private async Task<Fixture> SetupAsync(string suffix, decimal quantity = 2,
        decimal price = 100)
    {
        var actor = await _db.AdminActorAsync();
        var chart = await _db.SeedAccountingAsync();
        await _db.AccountingSettings().SetDefaultAsync(actor, PostingKeys.CustomerAdvances,
            chart["1110"].Id);

        var branch = await _db.CreateBranchAsync("RS-B-" + suffix, "فرع الحجز " + suffix);
        var warehouse = await _db.CreateWarehouseAsync("RS-W-" + suffix, "مخزن الحجز " + suffix,
            branch);
        var item = await _db.CreateItemAsync("RS-IT-" + suffix, "صنف الحجز " + suffix);
        await _db.PostReceiptAsync(item, branch, warehouse, 10, 50);
        var customer = await _db.PartnerRepo.CreateAsync(PartnerKind.Customer, new PartnerInput
        { Code = "RS-C-" + suffix, NameAr = "عميل الحجز " + suffix });

        var treasuryId = Guid.CreateVersion7();
        await _db.TreasuryRepo.CreateTreasuryAsync(treasuryId, new TreasuryInput
        {
            BranchId = branch, Code = "RS-T-" + suffix, NameAr = "خزينة الحجز " + suffix,
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
        var confirmed = await _db.BookingService().GetAsync(actor, booking.Id);
        return new Fixture(branch, warehouse, item, customer, confirmed);
    }

    [Fact]
    public async Task View_reports_reserved_consumed_and_remaining()
    {
        var f = await SetupAsync("1", quantity: 3, price: 40);

        var rows = await Repo().RemainingLinesAsync(f.Booking.Id);
        var line = Assert.Single(rows);

        Assert.Equal(3m, line.ReservedQty);
        Assert.Equal(0m, line.ConsumedQty);
        Assert.Equal(3m, line.RemainingQty);
        Assert.Equal(120m, line.RemainingValue);
        Assert.Equal(f.Customer.NameAr, line.CustomerName);
        Assert.Equal(f.Item.Code, line.ItemCode);
    }

    [Fact]
    public async Task Sellable_view_subtracts_the_reserved_quantity()
    {
        var f = await SetupAsync("3", quantity: 4);

        await using var connection = new NpgsqlConnection(TestDb.AdminConnection);
        await connection.OpenAsync();
        var row = await connection.QuerySingleAsync<(decimal OnHand, decimal Reserved, decimal Sellable)>("""
            SELECT on_hand, reserved_qty, sellable_qty FROM v_item_sellable
            WHERE item_id = @item AND warehouse_id = @wh
            """, new { item = f.Item.Id, wh = f.Warehouse });

        Assert.Equal(10m, row.OnHand);
        Assert.Equal(4m, row.Reserved);
        Assert.Equal(6m, row.Sellable);
    }

    [Fact]
    public async Task Listing_respects_the_branch_scope_of_the_caller()
    {
        var f = await SetupAsync("5", quantity: 1);
        var other = await _db.CreateBranchAsync("RS-B-OTHER", "فرع آخر");

        var ownBranch = await Repo().ListAsync(new PageRequest(), [f.Branch], null, true);
        var otherBranch = await Repo().ListAsync(new PageRequest(), [other], null, true);

        Assert.Contains(ownBranch.Items, r => r.BookingId == f.Booking.Id);
        Assert.DoesNotContain(otherBranch.Items, r => r.BookingId == f.Booking.Id);
    }
}
