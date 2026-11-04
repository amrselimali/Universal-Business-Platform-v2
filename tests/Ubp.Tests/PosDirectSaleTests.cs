using Dapper;
using Npgsql;
using Ubp.Core.Models;
using Xunit;

namespace Ubp.Tests;

/// <summary>
/// نقطة البيع: الصنف المكوَّد «تنفيذ مباشر» فقط هو الذي يُنفَّذ فور الحجز (صرف مخزون + تكلفة +
/// إيراد + مستحق على العميل + كارتة كميات) ويتجاوز قيود التنفيذ؛ أما الأصناف الأخرى فتبقى
/// حجزًا ينتظر التنفيذ من شاشة الخدمات المحجوزة كما هو معتاد.
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

    private static async Task SetExecutionModeAsync(Guid itemId, string mode)
    {
        await using var connection = new NpgsqlConnection(TestDb.AdminConnection);
        await connection.OpenAsync();
        await connection.ExecuteAsync(
            "UPDATE items SET execution_mode = @mode WHERE id = @id",
            new { id = itemId, mode });
    }

    private async Task<PosReceipt> CheckoutAsync(Fixture fixture, decimal quantity)
    {
        var actor = await _db.AdminActorAsync();
        return await _db.Pos().CheckoutAsync(actor, new PosCheckoutInput
        {
            BranchId = fixture.Branch, CustomerId = fixture.Customer.Id,
            WarehouseId = fixture.Warehouse, DocDate = DateTime.Today,
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
    public async Task Pos_executes_a_direct_item_and_posts_revenue_and_receivable()
    {
        var fixture = await SetupAsync("1");
        await SetExecutionModeAsync(fixture.Item.Id, ItemExecutionModes.Direct);

        var receipt = await CheckoutAsync(fixture, 4);

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
    public async Task Pos_keeps_a_reserved_item_as_a_booking_waiting_for_execution()
    {
        var fixture = await SetupAsync("2");
        Assert.Equal(ItemExecutionModes.Reserved, fixture.Item.ExecutionMode);

        var receipt = await CheckoutAsync(fixture, 4);

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
        Assert.Equal(0, await connection.ExecuteScalarAsync<int>(
            "SELECT count(*)::int FROM stock_movements WHERE item_id = @id AND direction = 'OUT'",
            new { id = fixture.Item.Id }));
    }

    [Fact]
    public async Task Pos_executes_only_the_direct_lines_and_keeps_the_others_booked()
    {
        var fixture = await SetupAsync("3");
        var direct = await _db.CreateItemAsync("PDS-IT-3D", "صنف مباشر مختلط");
        await _db.PostReceiptAsync(direct, fixture.Branch, fixture.Warehouse, 5, 40);
        await SetExecutionModeAsync(direct.Id, ItemExecutionModes.Direct);

        var actor = await _db.AdminActorAsync();
        var receipt = await _db.Pos().CheckoutAsync(actor, new PosCheckoutInput
        {
            BranchId = fixture.Branch, CustomerId = fixture.Customer.Id,
            WarehouseId = fixture.Warehouse, DocDate = DateTime.Today,
            Lines =
            {
                new PosLineInput
                {
                    ItemId = direct.Id, UnitId = direct.BaseUnitId, Quantity = 2, UnitPrice = 100
                },
                new PosLineInput
                {
                    ItemId = fixture.Item.Id, UnitId = fixture.Item.BaseUnitId,
                    Quantity = 3, UnitPrice = 100
                }
            }
        });

        Assert.True(receipt.Executed);
        Assert.Equal(3m, await _db.OnHandAsync(direct.Id, fixture.Warehouse));
        Assert.Equal(10m, await _db.OnHandAsync(fixture.Item.Id, fixture.Warehouse));

        await using var connection = new NpgsqlConnection(TestDb.AdminConnection);
        await connection.OpenAsync();
        var lines = (await connection.QueryAsync<(Guid ItemId, decimal Delivered, decimal Reserved)>(
            "SELECT item_id AS ItemId, delivered_qty AS Delivered, reserved_qty AS Reserved " +
            "FROM booking_lines WHERE booking_id = @id ORDER BY line_no",
            new { id = receipt.BookingId })).ToList();
        var directLine = lines.Single(l => l.ItemId == direct.Id);
        var reservedLine = lines.Single(l => l.ItemId == fixture.Item.Id);
        Assert.Equal(2m, directLine.Delivered);
        Assert.Equal(0m, directLine.Reserved);
        Assert.Equal(0m, reservedLine.Delivered);
        Assert.Equal(3m, reservedLine.Reserved);
        Assert.Equal(BookingStatuses.PartiallyDelivered,
            await connection.ExecuteScalarAsync<string>(
                "SELECT status FROM bookings WHERE id = @id", new { id = receipt.BookingId }));
    }

    [Fact]
    public async Task Pos_reads_the_execution_mode_saved_from_the_item_screen()
    {
        var actor = await _db.AdminActorAsync();
        await _db.SeedAccountingAsync();
        var branch = await _db.CreateBranchAsync("PDS-B-5", "فرع ربط الشاشة");
        var warehouse = await _db.CreateWarehouseAsync("PDS-W-5", "مخزن ربط الشاشة", branch);
        var unit = await _db.UnitRepo.CreateAsync(new UnitInput
        { Code = "PDS-U-5", NameAr = "وحدة ربط الشاشة" });

        // نفس ما تحفظه شاشة الأصناف عند اختيار «يُنفَّذ مباشرة».
        var item = await _db.ItemService().CreateAsync(actor, new ItemInput
        {
            Code = "PDS-IT-5", NameAr = "صنف ربط الشاشة", BaseUnitId = unit.Id,
            ExecutionMode = ItemExecutionModes.Direct, DefaultPrice = 100,
            Units = new List<ItemUnitInput> { new() { UnitId = unit.Id, ConversionFactor = 1 } }
        });
        Assert.Equal(ItemExecutionModes.Direct, item.ExecutionMode);
        await _db.PostReceiptAsync(item, branch, warehouse, 6, 40);

        // نفس القائمة التي تعرضها نقطة البيع (يجب أن تحمل طريقة التنفيذ).
        var listed = (await _db.ItemService().OptionsAsync(actor, "PDS-IT-5", true))
            .Single(o => o.Id == item.Id);
        Assert.Equal(ItemExecutionModes.Direct, listed.ExecutionMode);

        var customer = await _db.PartnerRepo.CreateAsync(PartnerKind.Customer, new PartnerInput
        { Code = "PDS-C-5", NameAr = "عميل ربط الشاشة" });
        var receipt = await _db.Pos().CheckoutAsync(actor, new PosCheckoutInput
        {
            BranchId = branch, CustomerId = customer.Id, WarehouseId = warehouse,
            DocDate = DateTime.Today,
            Lines =
            {
                new PosLineInput
                { ItemId = item.Id, UnitId = unit.Id, Quantity = 2, UnitPrice = 100 }
            }
        });

        Assert.True(receipt.Executed);
        Assert.Equal(4m, await _db.OnHandAsync(item.Id, warehouse));
    }

    [Fact]
    public async Task Pos_direct_item_sells_on_negative_stock_when_the_setting_allows_it()
    {
        var actor = await _db.AdminActorAsync();
        var settings = _db.Settings();
        var current = (await settings.GetAsync()).Clone();
        current.AllowNegativeStock = true;
        await settings.UpdateAsync(actor, current);
        try
        {
            var fixture = await SetupAsync("4", received: 0);
            await SetExecutionModeAsync(fixture.Item.Id, ItemExecutionModes.Direct);

            var receipt = await CheckoutAsync(fixture, 2);

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
