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
            MethodType = PaymentMethodTypes.Card, ClearingAccountId = chart["1130"].Id,
            FeePercent = 2m
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
        Assert.Equal(8m, await _db.BookingService().AvailableAsync(await _db.AdminActorAsync(),
            f.Item.Id, f.Warehouse));
    }

    [Fact]
    public async Task Pos_checkout_accepts_multiple_methods_with_fees()
    {
        var f = await SetupAsync("2");
        var receipt = await CheckoutAsync(f, 1,
            new PosPaymentInput { TreasuryId = f.TreasuryId, TreasuryMethodId = f.CashMethod,
                Amount = 60 },
            new PosPaymentInput { TreasuryId = f.TreasuryId, TreasuryMethodId = f.CardMethod,
                Amount = 40 });

        Assert.Equal(100m, receipt.Paid);
        Assert.Equal(0.8m, receipt.FeeTotal);
        var voucher = Assert.Single(receipt.Vouchers);
        Assert.Equal(2, voucher.Lines);

        await using var connection = new NpgsqlConnection(TestDb.AdminConnection);
        await connection.OpenAsync();
        var entries = (await connection.QueryAsync<(string Code, decimal Debit, decimal Credit)>(
            "SELECT a.code, l.debit, l.credit FROM journal_entry_lines l " +
            "JOIN accounts a ON a.id = l.account_id WHERE l.entry_id = " +
            "(SELECT journal_entry_id FROM vouchers WHERE id = @id)",
            new { id = voucher.VoucherId })).ToList();

        Assert.Contains(entries, l => l.Code == "1101" && l.Debit == 60m);
        Assert.Contains(entries, l => l.Code == "1130" && l.Debit == 39.2m);
        Assert.Contains(entries, l => l.Code == "5200" && l.Debit == 0.8m);
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

    [Fact]
    public async Task Pos_applies_item_tax_and_default_price_automatically()
    {
        var actor = await _db.AdminActorAsync();
        var f = await SetupAsync("4");
        var unit = await _db.UnitRepo.CreateAsync(new UnitInput
        { Code = "POS-U4", NameAr = "وحدة" });
        var tax = await _db.MasterData().SaveTaxAsync(actor, null, 0, new TaxInput
        { Code = "POS-VAT-4", NameAr = "ضريبة 14%", Rate = 14m });
        var item = await _db.ItemRepo.CreateAsync(new ItemInput
        {
            Code = "POS-TAX-4", NameAr = "صنف ضريبي", BaseUnitId = unit.Id, TaxId = tax.Id,
            DefaultPrice = 100,
            Units = { new ItemUnitInput { UnitId = unit.Id, ConversionFactor = 1 } }
        });
        await _db.PostReceiptAsync(item, f.Branch, f.Warehouse, 5, 50);

        var receipt = await _db.Pos().CheckoutAsync(actor, new PosCheckoutInput
        {
            BranchId = f.Branch, CustomerId = f.Customer.Id, WarehouseId = f.Warehouse,
            DocDate = DateTime.Today,
            Lines = { new PosLineInput { ItemId = item.Id, UnitId = unit.Id, Quantity = 2 } }
        });

        Assert.Equal(228m, receipt.Total);
        Assert.Equal(28m, receipt.TaxTotal);

        await using var connection = new NpgsqlConnection(TestDb.AdminConnection);
        await connection.OpenAsync();
        var line = await connection.QuerySingleAsync<(decimal Net, decimal Tax, decimal Total,
            Guid? TaxId)>(
            "SELECT net_amount, tax_amount, total_amount, tax_id FROM booking_lines " +
            "WHERE booking_id = @id", new { id = receipt.BookingId });
        Assert.Equal(200m, line.Net);
        Assert.Equal(28m, line.Tax);
        Assert.Equal(228m, line.Total);
        Assert.Equal(tax.Id, line.TaxId);
    }

    [Fact]
    public async Task Pos_service_recipe_consumes_components_and_books_cost()
    {
        var actor = await _db.AdminActorAsync();
        var f = await SetupAsync("5");
        var unit = f.Item.BaseUnitId;
        var service = await _db.ItemRepo.CreateAsync(new ItemInput
        {
            Code = "POS-SVC-5", NameAr = "خدمة", BaseUnitId = unit, ItemType = "SERVICE",
            DefaultPrice = 200,
            Units = { new ItemUnitInput { UnitId = unit, ConversionFactor = 1 } }
        });

        await using var connection = new NpgsqlConnection(TestDb.AdminConnection);
        await connection.OpenAsync();
        var recipeId = Guid.CreateVersion7();
        await connection.ExecuteAsync(
            "INSERT INTO service_recipes (id, item_id) VALUES (@id, @item)",
            new { id = recipeId, item = service.Id });
        await connection.ExecuteAsync("""
            INSERT INTO service_recipe_items (recipe_id, line_no, component_item_id, unit_id,
                quantity, consumption_mode)
            VALUES (@recipeId, 1, @component, @unit, 0.5, 0)
            """, new { recipeId, component = f.Item.Id, unit });

        var receipt = await _db.Pos().CheckoutAsync(actor, new PosCheckoutInput
        {
            BranchId = f.Branch, CustomerId = f.Customer.Id, WarehouseId = f.Warehouse,
            DocDate = DateTime.Today,
            Lines = { new PosLineInput { ItemId = service.Id, UnitId = unit, Quantity = 2 } }
        });

        Assert.Equal(400m, receipt.Total);
        Assert.Equal(9m, await _db.OnHandAsync(f.Item.Id, f.Warehouse));

        var cogs = await connection.ExecuteScalarAsync<decimal>("""
            SELECT COALESCE(SUM(l.debit), 0) FROM journal_entry_lines l
            JOIN accounts a ON a.id = l.account_id
            WHERE a.code = '5100' AND l.entry_id IN (
                SELECT id FROM journal_entries WHERE source_type = 'stock_document'
                  AND reference = 'POS-SERVICE')
            """);
        Assert.Equal(50m, cogs);
    }
}
