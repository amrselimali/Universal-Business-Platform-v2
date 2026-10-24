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
        Guid TreasuryId, Guid CashMethod, Guid CardMethod);

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
        var cash = Guid.CreateVersion7();
        await _db.TreasuryRepo.CreateMethodAsync(cash, new TreasuryMethodInput
        { TreasuryId = treasuryId, Code = "CASH", NameAr = "نقدي" }, actor.UserId);
        var card = Guid.CreateVersion7();
        await _db.TreasuryRepo.CreateMethodAsync(card, new TreasuryMethodInput
        {
            TreasuryId = treasuryId, Code = "CARD", NameAr = "بطاقة",
            MethodType = PaymentMethodTypes.Card, ClearingAccountId = chart["1130"].Id
        }, actor.UserId);

        return new Fixture(branch, warehouse, item, customer, treasuryId, cash, card);
    }

    private async Task<PosReceipt> CheckoutAsync(Fixture f, decimal quantity,
        params PosPaymentInput[] payments)
    {
        var actor = await _db.AdminActorAsync();
        return await _db.Pos().CheckoutAsync(actor, new PosCheckoutInput
        {
            BranchId = f.Branch, CustomerId = f.Customer.Id, WarehouseId = f.Warehouse,
            DocDate = DateTime.Today,
            Lines = { new PosLineInput { ItemId = f.Item.Id, UnitId = f.Item.BaseUnitId,
                Quantity = quantity, UnitPrice = 100 } },
            Payments = payments.ToList()
        });
    }

    [Fact]
    public async Task Pos_checkout_creates_booking_only_when_no_payment()
    {
        var f = await SetupAsync("1");
        var receipt = await CheckoutAsync(f, 2);

        Assert.Equal(200m, receipt.Total);
        Assert.Equal(0m, receipt.Paid);
        Assert.Empty(receipt.Vouchers);
        Assert.StartsWith("BK-", receipt.BookingNo);

        await using var connection = new NpgsqlConnection(TestDb.AdminConnection);
        await connection.OpenAsync();
        var booking = await connection.QuerySingleAsync<(string Status, string Source)>(
            "SELECT status, source FROM bookings WHERE id = @id", new { id = receipt.BookingId });
        Assert.Equal(BookingStatuses.Confirmed, booking.Status);
        Assert.Equal(BookingSources.Pos, booking.Source);

        Assert.Equal(0, await connection.ExecuteScalarAsync<int>(
            "SELECT count(*) FROM vouchers WHERE branch_id = @b", new { b = f.Branch }));
        Assert.Equal(0, await connection.ExecuteScalarAsync<int>(
            "SELECT count(*) FROM commercial_documents WHERE branch_id = @b", new { b = f.Branch }));
        Assert.Equal(0, await connection.ExecuteScalarAsync<int>(
            "SELECT count(*) FROM delivery_notes WHERE branch_id = @b", new { b = f.Branch }));

        Assert.Equal(8m, await _db.BookingService().AvailableAsync(await _db.AdminActorAsync(),
            f.Item.Id, f.Warehouse));
    }

    [Fact]
    public async Task Pos_checkout_accepts_multiple_methods_in_one_voucher()
    {
        var f = await SetupAsync("2");
        var receipt = await CheckoutAsync(f, 1,
            new PosPaymentInput { TreasuryId = f.TreasuryId, TreasuryMethodId = f.CashMethod,
                Amount = 60 },
            new PosPaymentInput { TreasuryId = f.TreasuryId, TreasuryMethodId = f.CardMethod,
                Amount = 40 });

        Assert.Equal(100m, receipt.Paid);
        var voucher = Assert.Single(receipt.Vouchers);
        Assert.Equal(2, voucher.Lines);
        Assert.StartsWith("RCV-", voucher.VoucherNo);

        await using var connection = new NpgsqlConnection(TestDb.AdminConnection);
        await connection.OpenAsync();
        var entries = (await connection.QueryAsync<(string Code, decimal Debit, decimal Credit)>("""
            SELECT a.code, l.debit, l.credit FROM journal_entry_lines l
            JOIN accounts a ON a.id = l.account_id WHERE l.entry_id = (
                SELECT journal_entry_id FROM vouchers WHERE id = @id)
            """, new { id = voucher.VoucherId })).ToList();

        Assert.Contains(entries, l => l.Code == "1101" && l.Debit == 60m);
        Assert.Contains(entries, l => l.Code == "1130" && l.Debit == 40m);
        Assert.Contains(entries, l => l.Code == "1110" && l.Credit == 100m);
    }

    [Fact]
    public async Task Pos_payment_above_total_is_rejected_and_nothing_created()
    {
        var f = await SetupAsync("3");

        var error = await Assert.ThrowsAsync<ValidationAppException>(() => CheckoutAsync(f, 1,
            new PosPaymentInput { TreasuryId = f.TreasuryId, TreasuryMethodId = f.CashMethod,
                Amount = 300 }));
        Assert.Equal("validation.pos_payment_exceeds_total", error.Code);

        await using var connection = new NpgsqlConnection(TestDb.AdminConnection);
        await connection.OpenAsync();
        Assert.Equal(0, await connection.ExecuteScalarAsync<int>(
            "SELECT count(*) FROM bookings WHERE branch_id = @b", new { b = f.Branch }));
        Assert.Equal(0, await connection.ExecuteScalarAsync<int>(
            "SELECT count(*) FROM vouchers WHERE branch_id = @b", new { b = f.Branch }));
    }
}
