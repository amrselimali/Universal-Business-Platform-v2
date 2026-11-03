using Dapper;
using Npgsql;
using Ubp.Core.Models;
using Ubp.Core.Results;
using Xunit;

namespace Ubp.Tests;

/// <summary>
/// تحصيل الخدمات المحجوزة من نقطة البيع: يعرض الإجمالي والمسدد سابقًا والمستحق،
/// ويُنشئ سند قبض مرحّلًا مخصّصًا على الحجوزات.
/// </summary>
public sealed class BookingCollectionTests : IClassFixture<TestDb>
{
    private readonly TestDb _db;

    public BookingCollectionTests(TestDb db) => _db = db;

    private async Task<(Guid Branch, Guid Warehouse, Partner Customer, Booking Booking,
        Guid TreasuryId, Guid MethodId)> SetupAsync(string suffix)
    {
        var actor = await _db.AdminActorAsync();
        var chart = await _db.SeedAccountingAsync();
        var branch = await _db.CreateBranchAsync("BC-B" + suffix, "فرع التحصيل " + suffix);
        var warehouse = await _db.CreateWarehouseAsync("BC-W" + suffix, "مخزن التحصيل " + suffix,
            branch);
        var unit = await _db.UnitRepo.CreateAsync(new UnitInput
        { Code = "BC-U" + suffix, NameAr = "وحدة التحصيل " + suffix });
        var service = await _db.ItemRepo.CreateAsync(new ItemInput
        {
            Code = "BC-SV" + suffix, NameAr = "خدمة التحصيل " + suffix, ItemType = "SERVICE",
            BaseUnitId = unit.Id,
            Units = new List<ItemUnitInput> { new() { UnitId = unit.Id, ConversionFactor = 1 } }
        });
        var customer = await _db.PartnerRepo.CreateAsync(PartnerKind.Customer, new PartnerInput
        { Code = "BC-C" + suffix, NameAr = "عميل التحصيل " + suffix });

        var booking = await _db.BookingService().CreateAsync(actor, new BookingInput
        {
            BranchId = branch, BookingDate = DateTime.Today, CustomerId = customer.Id,
            Lines =
            {
                new BookingLineInput
                {
                    ItemId = service.Id, UnitId = service.BaseUnitId, WarehouseId = warehouse,
                    Quantity = 2, UnitPrice = 150
                }
            }
        });
        await _db.BookingRepo.ConfirmAsync(booking.Id, actor.UserId, reserveStock: false);

        var treasuryId = Guid.CreateVersion7();
        await _db.TreasuryRepo.CreateTreasuryAsync(treasuryId, new TreasuryInput
        {
            BranchId = branch, Code = "BC-T" + suffix, NameAr = "خزينة التحصيل " + suffix,
            TreasuryType = TreasuryTypes.Cash, AccountId = chart["1101"].Id
        }, actor.UserId);
        var methodId = Guid.CreateVersion7();
        await _db.TreasuryRepo.CreateMethodAsync(methodId, new TreasuryMethodInput
        { TreasuryId = treasuryId, Code = "CASH", NameAr = "نقدي" }, actor.UserId);

        return (branch, warehouse, customer, booking, treasuryId, methodId);
    }

    private static BookingCollectionInput CollectInput(Guid branch, Guid customer, Guid treasury,
        Guid method, Guid bookingId, decimal amount) => new()
    {
        BranchId = branch, CustomerId = customer, TreasuryId = treasury,
        TreasuryMethodId = method, DocDate = DateTime.Today,
        Lines = { new BookingCollectionLineInput { BookingId = bookingId, Amount = amount } }
    };

    [Fact]
    public async Task Lists_outstanding_services_with_paid_and_remaining()
    {
        var actor = await _db.AdminActorAsync();
        var f = await SetupAsync("1");
        var service = _db.BookingCollections();

        var first = await service.CollectAsync(actor,
            CollectInput(f.Branch, f.Customer.Id, f.TreasuryId, f.MethodId, f.Booking.Id, 100));
        Assert.Equal(100m, first.Total);

        var rows = await service.ListOutstandingAsync(actor, f.Branch, f.Customer.Id);
        var row = Assert.Single(rows);
        Assert.Equal(f.Booking.Id, row.BookingId);
        Assert.Equal(300m, row.TotalAmount);
        Assert.Equal(100m, row.PaidAmount);
        Assert.Equal(200m, row.RemainingAmount);
        Assert.Contains("خدمة التحصيل 1", row.Services);

        await service.CollectAsync(actor,
            CollectInput(f.Branch, f.Customer.Id, f.TreasuryId, f.MethodId, f.Booking.Id, 200));

        Assert.Empty(await service.ListOutstandingAsync(actor, f.Branch, f.Customer.Id));
    }

    [Fact]
    public async Task Collection_posts_a_receipt_voucher_allocated_to_the_booking()
    {
        var actor = await _db.AdminActorAsync();
        var f = await SetupAsync("2");

        var result = await _db.BookingCollections().CollectAsync(actor,
            CollectInput(f.Branch, f.Customer.Id, f.TreasuryId, f.MethodId, f.Booking.Id, 200));

        Assert.Equal(200m, result.Total);
        Assert.Equal(0m, result.Fee);
        Assert.StartsWith("RCV-", result.VoucherNo);
        Assert.Equal(1, result.Bookings);

        await using var connection = new NpgsqlConnection(TestDb.AdminConnection);
        await connection.OpenAsync();
        Assert.Equal("posted", await connection.ExecuteScalarAsync<string>(
            "SELECT status FROM vouchers WHERE id = @id", new { id = result.VoucherId }));
        Assert.Equal(200m, await connection.ExecuteScalarAsync<decimal>(
            "SELECT amount FROM booking_payments WHERE voucher_id = @id",
            new { id = result.VoucherId }));

        var entries = (await connection.QueryAsync<(string Code, decimal Debit, decimal Credit)>("""
            SELECT a.code, l.debit, l.credit
            FROM journal_entry_lines l
            JOIN accounts a ON a.id = l.account_id
            WHERE l.entry_id = (SELECT journal_entry_id FROM vouchers WHERE id = @id)
            """, new { id = result.VoucherId })).ToList();
        Assert.Contains(entries, l => l.Code == "1101" && l.Debit == 200m);
        Assert.Contains(entries, l => l.Code == "1110" && l.Credit == 200m);
    }

    [Fact]
    public async Task Collection_above_the_remaining_amount_is_rejected()
    {
        var actor = await _db.AdminActorAsync();
        var f = await SetupAsync("3");

        var error = await Assert.ThrowsAsync<ValidationAppException>(() =>
            _db.BookingCollections().CollectAsync(actor,
                CollectInput(f.Branch, f.Customer.Id, f.TreasuryId, f.MethodId, f.Booking.Id, 400)));
        Assert.Equal("validation.collection_exceeds_remaining", error.Code);

        var rows = await _db.BookingCollections()
            .ListOutstandingAsync(actor, f.Branch, f.Customer.Id);
        Assert.Equal(300m, Assert.Single(rows).RemainingAmount);
    }

    [Fact]
    public async Task Pos_checkout_records_the_collected_amount_on_the_booking()
    {
        var actor = await _db.AdminActorAsync();
        var chart = await _db.SeedAccountingAsync();
        var branch = await _db.CreateBranchAsync("BC-P-B", "فرع POS التحصيل");
        var warehouse = await _db.CreateWarehouseAsync("BC-P-W", "مخزن POS التحصيل", branch);
        var unit = await _db.UnitRepo.CreateAsync(new UnitInput
        { Code = "BC-P-U", NameAr = "وحدة POS التحصيل" });
        var service = await _db.ItemRepo.CreateAsync(new ItemInput
        {
            Code = "BC-P-SV", NameAr = "خدمة POS التحصيل", ItemType = "SERVICE",
            BaseUnitId = unit.Id,
            Units = new List<ItemUnitInput> { new() { UnitId = unit.Id, ConversionFactor = 1 } }
        });
        var customer = await _db.PartnerRepo.CreateAsync(PartnerKind.Customer, new PartnerInput
        { Code = "BC-P-C", NameAr = "عميل POS التحصيل" });
        var treasuryId = Guid.CreateVersion7();
        await _db.TreasuryRepo.CreateTreasuryAsync(treasuryId, new TreasuryInput
        {
            BranchId = branch, Code = "BC-P-T", NameAr = "خزينة POS التحصيل",
            TreasuryType = TreasuryTypes.Cash, AccountId = chart["1101"].Id
        }, actor.UserId);
        var methodId = Guid.CreateVersion7();
        await _db.TreasuryRepo.CreateMethodAsync(methodId, new TreasuryMethodInput
        { TreasuryId = treasuryId, Code = "CASH", NameAr = "نقدي" }, actor.UserId);

        var receipt = await _db.Pos().CheckoutAsync(actor, new PosCheckoutInput
        {
            BranchId = branch, CustomerId = customer.Id, WarehouseId = warehouse,
            DocDate = DateTime.Today,
            Lines =
            {
                new PosLineInput
                {
                    ItemId = service.Id, UnitId = service.BaseUnitId, Quantity = 1, UnitPrice = 100
                }
            },
            Payments =
            {
                new PosPaymentInput
                {
                    TreasuryId = treasuryId, TreasuryMethodId = methodId, Amount = 100
                }
            }
        });

        await using var connection = new NpgsqlConnection(TestDb.AdminConnection);
        await connection.OpenAsync();
        Assert.Equal(100m, await connection.ExecuteScalarAsync<decimal>(
            "SELECT COALESCE(SUM(amount), 0) FROM booking_payments WHERE booking_id = @id",
            new { id = receipt.BookingId }));
    }
}
