using Dapper;
using Npgsql;
using Ubp.Core.Models;
using Xunit;

namespace Ubp.Tests;

/// <summary>
/// البيع المباشر من نقطة البيع: الصنف «المكوَّد» يُنفَّذ فور الحفظ (حجز + تنفيذ في معاملة واحدة)
/// ويتجاوز قيود التنفيذ، فيُصرف المخزون ويُرحّل الإيراد والمستحق على العميل وتُحدَّث كارتة الكميات
/// دون أن يبقى أي رصيد محجوز.
/// </summary>
public sealed class PosDirectSaleTests : IClassFixture<TestDb>
{
    private readonly TestDb _db;

    public PosDirectSaleTests(TestDb db) => _db = db;

    private sealed record Fixture(Guid Branch, Guid Warehouse, Item Item, Partner Customer);

    private async Task<Fixture> SetupAsync(string suffix, decimal received = 10)
    {
        await _db.SeedAccountingAsync();
        var branch = await _db.CreateBranchAsync("PDS-B-" + suffix, "فرع بيع مباشر " + suffix);
        var warehouse = await _db.CreateWarehouseAsync("PDS-W-" + suffix,
            "مخزن بيع مباشر " + suffix, branch);
        var item = await _db.CreateItemAsync("PDS-IT-" + suffix, "صنف بيع مباشر " + suffix);
        if (received > 0)
            await _db.PostReceiptAsync(item, branch, warehouse, received, 40);
        var customer = await _db.PartnerRepo.CreateAsync(PartnerKind.Customer, new PartnerInput
        { Code = "PDS-C-" + suffix, NameAr = "عميل بيع مباشر " + suffix });
        return new Fixture(branch, warehouse, item, customer);
    }

    private async Task<PosReceipt> CheckoutAsync(Fixture fixture, decimal quantity,
        bool directSale)
    {
        var actor = await _db.AdminActorAsync();
        return await _db.Pos().CheckoutAsync(actor, new PosCheckoutInput
        {
            BranchId = fixture.Branch, CustomerId = fixture.Customer.Id,
            WarehouseId = fixture.Warehouse, DocDate = DateTime.Today,
            DirectSale = directSale,
            Lines =
            {
                new PosLineInput
                {
                    ItemId = fixture.Item.Id, UnitId = fixture.Item.BaseUnitId,
                    Quantity = quantity, UnitPrice = 100
                }
            }
        });
    }

    [Fact]
    public async Task Pos_direct_sale_executes_a_reserved_item_and_posts_revenue()
    {
        var fixture = await SetupAsync("1");
        Assert.Equal(ItemExecutionModes.Reserved, fixture.Item.ExecutionMode);

        var receipt = await CheckoutAsync(fixture, 4, directSale: true);

        Assert.True(receipt.Executed);
        Assert.Equal(400m, receipt.Total);
        Assert.Equal(6m, await _db.OnHandAsync(fixture.Item.Id, fixture.Warehouse));

        await using var connection = new NpgsqlConnection(TestDb.AdminConnection);
        await connection.OpenAsync();

        var line = await connection.QuerySingleAsync<(decimal Delivered, decimal Reserved)>(
            "SELECT delivered_qty, reserved_qty FROM booking_lines WHERE booking_id = @id",
            new { id = receipt.BookingId });
        Assert.Equal(4m, line.Delivered);
        Assert.Equal(0m, line.Reserved);
        Assert.Equal(BookingStatuses.Completed, await connection.ExecuteScalarAsync<string>(
            "SELECT status FROM bookings WHERE id = @id", new { id = receipt.BookingId }));

        var entries = (await connection.QueryAsync<(string Code, decimal Debit, decimal Credit)>(
            """
            SELECT a.code, l.debit, l.credit FROM journal_entry_lines l
            JOIN accounts a ON a.id = l.account_id
            JOIN journal_entries e ON e.id = l.entry_id
            WHERE e.source_type = 'execution'
              AND e.reference =
                  (SELECT doc_no FROM booking_executions WHERE booking_id = @bookingId)
            """, new { bookingId = receipt.BookingId })).ToList();
        Assert.Contains(entries, l => l.Code == "1110" && l.Debit == 400m);
        Assert.Contains(entries, l => l.Code == "4100" && l.Credit == 400m);
        Assert.Contains(entries, l => l.Code == "5100" && l.Debit == 160m);
        Assert.Contains(entries, l => l.Code == "2150" && l.Credit == 160m);

        Assert.Equal(1, await connection.ExecuteScalarAsync<int>(
            "SELECT count(*)::int FROM stock_movements WHERE item_id = @id AND direction = 'OUT'",
            new { id = fixture.Item.Id }));
    }

    [Fact]
    public async Task Pos_without_direct_sale_still_keeps_the_quantity_reserved()
    {
        var fixture = await SetupAsync("2");

        var receipt = await CheckoutAsync(fixture, 4, directSale: false);

        Assert.False(receipt.Executed);
        Assert.Equal(10m, await _db.OnHandAsync(fixture.Item.Id, fixture.Warehouse));

        await using var connection = new NpgsqlConnection(TestDb.AdminConnection);
        await connection.OpenAsync();
        Assert.Equal(BookingStatuses.Confirmed, await connection.ExecuteScalarAsync<string>(
            "SELECT status FROM bookings WHERE id = @id", new { id = receipt.BookingId }));

        var line = await connection.QuerySingleAsync<(decimal Delivered, decimal Reserved)>(
            "SELECT delivered_qty, reserved_qty FROM booking_lines WHERE booking_id = @id",
            new { id = receipt.BookingId });
        Assert.Equal(0m, line.Delivered);
        Assert.Equal(4m, line.Reserved);
        Assert.Equal(0, await connection.ExecuteScalarAsync<int>(
            "SELECT count(*)::int FROM booking_executions WHERE booking_id = @id",
            new { id = receipt.BookingId }));
    }

    [Fact]
    public async Task Pos_executes_a_direct_item_even_without_the_direct_sale_flag()
    {
        var fixture = await SetupAsync("4");
        await using (var setup = new NpgsqlConnection(TestDb.AdminConnection))
        {
            await setup.OpenAsync();
            await setup.ExecuteAsync(
                "UPDATE items SET execution_mode = 'direct' WHERE id = @id",
                new { id = fixture.Item.Id });
        }

        var receipt = await CheckoutAsync(fixture, 3, directSale: false);

        Assert.True(receipt.Executed);
        Assert.Equal(7m, await _db.OnHandAsync(fixture.Item.Id, fixture.Warehouse));

        await using var connection = new NpgsqlConnection(TestDb.AdminConnection);
        await connection.OpenAsync();
        var line = await connection.QuerySingleAsync<(decimal Delivered, decimal Reserved)>(
            "SELECT delivered_qty, reserved_qty FROM booking_lines WHERE booking_id = @id",
            new { id = receipt.BookingId });
        Assert.Equal(3m, line.Delivered);
        Assert.Equal(0m, line.Reserved);
        Assert.Equal(BookingStatuses.Completed, await connection.ExecuteScalarAsync<string>(
            "SELECT status FROM bookings WHERE id = @id", new { id = receipt.BookingId }));
        Assert.Equal(1, await connection.ExecuteScalarAsync<int>(
            "SELECT count(*)::int FROM booking_executions WHERE booking_id = @id",
            new { id = receipt.BookingId }));
    }

    [Fact]
    public async Task Pos_direct_sale_sells_on_negative_stock_when_the_setting_allows_it()
    {
        var actor = await _db.AdminActorAsync();
        var settings = _db.Settings();
        var current = (await settings.GetAsync()).Clone();
        current.AllowNegativeStock = true;
        await settings.UpdateAsync(actor, current);
        try
        {
            var fixture = await SetupAsync("3", received: 0);

            var receipt = await CheckoutAsync(fixture, 2, directSale: true);

            Assert.True(receipt.Executed);
            Assert.Equal(-2m, await _db.OnHandAsync(fixture.Item.Id, fixture.Warehouse));
        }
        finally
        {
            var reloaded = (await settings.GetAsync()).Clone();
            reloaded.AllowNegativeStock = false;
            await settings.UpdateAsync(actor, reloaded);
        }
    }
}
