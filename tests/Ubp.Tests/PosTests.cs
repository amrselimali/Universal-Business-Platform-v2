using Dapper;
using Npgsql;
using Ubp.Core.Models;
using Ubp.Core.Results;
using Xunit;

namespace Ubp.Tests;

public sealed class PosTests : IClassFixture<TestDb>
{
    private readonly TestDb _db;

    public PosTests(TestDb db) => _db = db;

    private sealed record Fixture(Guid Branch, Guid Warehouse, Item Item, Partner Customer,
        Guid TreasuryId, Guid MethodId);

    private async Task<Fixture> SetupAsync(string suffix)
    {
        var actor = await _db.AdminActorAsync();
        var chart = await _db.SeedAccountingAsync();
        await _db.AccountingSettings().SetDefaultAsync(actor, PostingKeys.CustomerAdvances,
            chart["1110"].Id);

        var branch = await _db.CreateBranchAsync("POS-B-" + suffix, "فرع نقاط " + suffix);
        var warehouse = await _db.CreateWarehouseAsync("POS-W-" + suffix, "مخزن " + suffix, branch);
        var item = await _db.CreateItemAsync("POS-IT-" + suffix, "صنف " + suffix);
        await _db.PostReceiptAsync(item, branch, warehouse, 10, 50);
        var customer = await _db.PartnerRepo.CreateAsync(PartnerKind.Customer, new PartnerInput
        { Code = "POS-C-" + suffix, NameAr = "عميل " + suffix });

        var treasuryId = Guid.CreateVersion7();
        await _db.TreasuryRepo.CreateTreasuryAsync(treasuryId, new TreasuryInput
        {
            BranchId = branch, Code = "POS-T-" + suffix, NameAr = "خزينة " + suffix,
            TreasuryType = TreasuryTypes.Cash, AccountId = chart["1101"].Id
        }, actor.UserId);
        var methodId = Guid.CreateVersion7();
        await _db.TreasuryRepo.CreateMethodAsync(methodId, new TreasuryMethodInput
        { TreasuryId = treasuryId, Code = "CASH", NameAr = "نقدي" }, actor.UserId);

        return new Fixture(branch, warehouse, item, customer, treasuryId, methodId);
    }

    private async Task<PosReceipt> CheckoutAsync(Fixture f, Guid itemId, decimal quantity)
    {
        var actor = await _db.AdminActorAsync();
        return await _db.Pos().CheckoutAsync(actor, new PosCheckoutInput
        {
            BranchId = f.Branch, CustomerId = f.Customer.Id, WarehouseId = f.Warehouse,
            TreasuryId = f.TreasuryId, TreasuryMethodId = f.MethodId, DocDate = DateTime.Today,
            Lines = { new PosLineInput { ItemId = itemId, UnitId = f.Item.BaseUnitId,
                Quantity = quantity, UnitPrice = 100 } }
        });
    }

    [Fact]
    public async Task Pos_checkout_creates_booking_invoice_and_payment_in_one_tx()
    {
        var f = await SetupAsync("1");
        var receipt = await CheckoutAsync(f, f.Item.Id, 2);

        Assert.Equal(200m, receipt.Total);
        Assert.StartsWith("BK-", receipt.BookingNo);
        Assert.StartsWith("DN-", receipt.DeliveryNo);
        Assert.StartsWith("SI-", receipt.InvoiceNo);
        Assert.StartsWith("RCV-", receipt.VoucherNo);
        Assert.Equal(8m, await _db.OnHandAsync(f.Item.Id, f.Warehouse));

        await using var connection = new NpgsqlConnection(TestDb.AdminConnection);
        await connection.OpenAsync();

        var booking = await connection.QuerySingleAsync<(string Status, string Source)>(
            "SELECT status, source FROM bookings WHERE id = @id", new { id = receipt.BookingId });
        Assert.Equal(BookingStatuses.Completed, booking.Status);
        Assert.Equal(BookingSources.Pos, booking.Source);

        var invoice = await connection.QuerySingleAsync<(string Status, decimal Total,
            decimal Allocated, Guid? Entry)>(
            "SELECT status, total, allocated_total, journal_entry_id FROM commercial_documents " +
            "WHERE id = @id", new { id = receipt.InvoiceId });
        Assert.Equal(CommercialStatuses.Posted, invoice.Status);
        Assert.Equal(200m, invoice.Total);
        Assert.Equal(200m, invoice.Allocated);
        Assert.NotNull(invoice.Entry);

        var voucher = await connection.QuerySingleAsync<(string Status, Guid? Entry)>(
            "SELECT status, journal_entry_id FROM vouchers WHERE id = @id",
            new { id = receipt.VoucherId });
        Assert.Equal(VoucherStatuses.Posted, voucher.Status);
        Assert.NotNull(voucher.Entry);

        var entry = await connection.QuerySingleAsync<(decimal Debit, decimal Credit)>(
            "SELECT total_debit, total_credit FROM journal_entries WHERE id = @id",
            new { id = voucher.Entry });
        Assert.Equal(200m, entry.Debit);
        Assert.Equal(entry.Debit, entry.Credit);
    }

    [Fact]
    public async Task Pos_failure_rolls_back_everything()
    {
        var f = await SetupAsync("2");
        var emptyItem = await _db.CreateItemAsync("POS-EMPTY-2", "صنف بلا رصيد");

        await Assert.ThrowsAnyAsync<AppException>(() => CheckoutAsync(f, emptyItem.Id, 1));

        await using var connection = new NpgsqlConnection(TestDb.AdminConnection);
        await connection.OpenAsync();
        var bookings = await connection.ExecuteScalarAsync<int>(
            "SELECT count(*) FROM bookings WHERE branch_id = @branch", new { branch = f.Branch });
        var invoices = await connection.ExecuteScalarAsync<int>(
            "SELECT count(*) FROM commercial_documents WHERE branch_id = @branch",
            new { branch = f.Branch });
        var vouchers = await connection.ExecuteScalarAsync<int>(
            "SELECT count(*) FROM vouchers WHERE branch_id = @branch", new { branch = f.Branch });

        Assert.Equal(0, bookings);
        Assert.Equal(0, invoices);
        Assert.Equal(0, vouchers);
    }
}
