using Dapper;
using Npgsql;
using Ubp.Core.Models;
using Xunit;

namespace Ubp.Tests;

/// <summary>
/// الأصناف «المباشرة»: تُنفَّذ من حجز مسودة بدون قيود الموظف/الموعد/العدّاد،
/// وتُرحّل الإيراد والذمم وحركة المخزون والتكلفة مباشرة.
/// </summary>
public sealed class DirectExecutionTests : IClassFixture<TestDb>
{
    private readonly TestDb _db;

    public DirectExecutionTests(TestDb db) => _db = db;

    private async Task<(Guid Branch, Guid Warehouse, Item Item, Partner Customer,
        Booking Booking, Guid LineId)> SetupAsync(string suffix, string itemType)
    {
        var actor = await _db.AdminActorAsync();
        await _db.SeedAccountingAsync();
        var branch = await _db.CreateBranchAsync("DX-B" + suffix, "فرع مباشر " + suffix);
        var warehouse = await _db.CreateWarehouseAsync("DX-W" + suffix, "مخزن مباشر " + suffix,
            branch);
        var unit = await _db.UnitRepo.CreateAsync(new UnitInput
        { Code = "DX-U" + suffix, NameAr = "وحدة مباشرة " + suffix });
        var item = await _db.ItemRepo.CreateAsync(new ItemInput
        {
            Code = "DX-IT" + suffix, NameAr = "صنف مباشر " + suffix, ItemType = itemType,
            ExecutionMode = ItemExecutionModes.Direct, BaseUnitId = unit.Id,
            Units = new List<ItemUnitInput> { new() { UnitId = unit.Id, ConversionFactor = 1 } }
        });
        if (itemType == "GOODS")
            await _db.PostReceiptAsync(item, branch, warehouse, 10, 50);

        var customer = await _db.PartnerRepo.CreateAsync(PartnerKind.Customer, new PartnerInput
        { Code = "DX-C" + suffix, NameAr = "عميل مباشر " + suffix });
        var booking = await _db.BookingService().CreateAsync(actor, new BookingInput
        {
            BranchId = branch, BookingDate = DateTime.Today, CustomerId = customer.Id,
            Lines =
            {
                new BookingLineInput
                {
                    ItemId = item.Id, UnitId = item.BaseUnitId, WarehouseId = warehouse,
                    Quantity = 2, UnitPrice = 100
                }
            }
        });
        var line = (await _db.BookingService().GetAsync(actor, booking.Id)).Lines.Single();
        return (branch, warehouse, item, customer, booking, line.Id);
    }

    [Fact]
    public async Task Direct_service_bypasses_employee_and_posts_revenue()
    {
        var actor = await _db.AdminActorAsync();
        var f = await SetupAsync("1", "SERVICE");
        Assert.Equal(BookingStatuses.Draft, f.Booking.Status);

        var result = await _db.Executions().ExecuteAsync(actor, new BookingExecutionInput
        { BookingLineId = f.LineId, Quantity = 2 });
        Assert.Equal(2m, result.Quantity);

        await using var connection = new NpgsqlConnection(TestDb.AdminConnection);
        await connection.OpenAsync();
        var entries = (await connection.QueryAsync<(string Code, decimal Debit, decimal Credit)>("""
            SELECT a.code, l.debit, l.credit FROM journal_entry_lines l
            JOIN accounts a ON a.id = l.account_id
            JOIN journal_entries e ON e.id = l.entry_id
            WHERE e.source_type = 'execution' AND e.reference = @docNo
            """, new { docNo = result.DocNo })).ToList();
        Assert.Contains(entries, l => l.Code == "1110" && l.Debit == 200m);
        Assert.Contains(entries, l => l.Code == "4100" && l.Credit == 200m);
    }

    [Fact]
    public async Task Direct_goods_records_stock_cost_and_revenue()
    {
        var actor = await _db.AdminActorAsync();
        var f = await SetupAsync("2", "GOODS");

        var result = await _db.Executions().ExecuteAsync(actor, new BookingExecutionInput
        { BookingLineId = f.LineId, Quantity = 2 });
        Assert.Equal(100m, result.CostTotal);
        Assert.Equal(8m, await _db.OnHandAsync(f.Item.Id, f.Warehouse));

        await using var connection = new NpgsqlConnection(TestDb.AdminConnection);
        await connection.OpenAsync();
        var entries = (await connection.QueryAsync<(string Code, decimal Debit, decimal Credit)>("""
            SELECT a.code, l.debit, l.credit FROM journal_entry_lines l
            JOIN accounts a ON a.id = l.account_id
            JOIN journal_entries e ON e.id = l.entry_id
            WHERE e.source_type = 'execution' AND e.reference = @docNo
            """, new { docNo = result.DocNo })).ToList();
        Assert.Contains(entries, l => l.Code == "1110" && l.Debit == 200m);
        Assert.Contains(entries, l => l.Code == "4100" && l.Credit == 200m);
        Assert.Contains(entries, l => l.Code == "5100" && l.Debit == 100m);
        Assert.Contains(entries, l => l.Code == "2150" && l.Credit == 100m);

        var movement = await connection.ExecuteScalarAsync<int>(
            "SELECT count(*)::int FROM stock_movements WHERE document_id = " +
            "(SELECT stock_document_id FROM booking_executions WHERE id = @id) AND direction = 'OUT'",
            new { id = result.ExecutionId });
        Assert.Equal(1, movement);
    }

    [Fact]
    public async Task Pos_checkout_of_a_direct_item_executes_immediately()
    {
        var actor = await _db.AdminActorAsync();
        await _db.SeedAccountingAsync();
        var branch = await _db.CreateBranchAsync("DX-B3", "فرع POS مباشر");
        var warehouse = await _db.CreateWarehouseAsync("DX-W3", "مخزن POS مباشر", branch);
        var item = await _db.CreateItemAsync("DX-IT3", "صنف POS مباشر");
        await _db.PostReceiptAsync(item, branch, warehouse, 20, 40);
        await using (var setup = new NpgsqlConnection(TestDb.AdminConnection))
        {
            await setup.OpenAsync();
            await setup.ExecuteAsync(
                "UPDATE items SET execution_mode = 'direct' WHERE id = @id", new { id = item.Id });
        }
        var customer = await _db.PartnerRepo.CreateAsync(PartnerKind.Customer, new PartnerInput
        { Code = "DX-C3", NameAr = "عميل POS مباشر" });

        var receipt = await _db.Pos().CheckoutAsync(actor, new PosCheckoutInput
        {
            BranchId = branch, CustomerId = customer.Id, WarehouseId = warehouse,
            DocDate = DateTime.Today,
            Lines =
            {
                new PosLineInput
                {
                    ItemId = item.Id, UnitId = item.BaseUnitId, Quantity = 10, UnitPrice = 100
                }
            }
        });

        await using var connection = new NpgsqlConnection(TestDb.AdminConnection);
        await connection.OpenAsync();
        var line = await connection.QuerySingleAsync<(decimal Delivered, decimal Reserved)>(
            "SELECT delivered_qty, reserved_qty FROM booking_lines WHERE booking_id = @id",
            new { id = receipt.BookingId });
        Assert.Equal(10m, line.Delivered);
        Assert.Equal(0m, line.Reserved);
        Assert.Equal(10m, await _db.OnHandAsync(item.Id, warehouse));
        Assert.Equal(BookingStatuses.Completed, await connection.ExecuteScalarAsync<string>(
            "SELECT status FROM bookings WHERE id = @id", new { id = receipt.BookingId }));

        var entries = (await connection.QueryAsync<(string Code, decimal Debit, decimal Credit)>("""
            SELECT a.code, l.debit, l.credit FROM journal_entry_lines l
            JOIN accounts a ON a.id = l.account_id
            JOIN journal_entries e ON e.id = l.entry_id
            WHERE e.source_type = 'execution'
            """)).ToList();
        Assert.Contains(entries, l => l.Code == "1110" && l.Debit == 1000m);
        Assert.Contains(entries, l => l.Code == "4100" && l.Credit == 1000m);
        Assert.Contains(entries, l => l.Code == "5100" && l.Debit == 400m);
        Assert.Contains(entries, l => l.Code == "2150" && l.Credit == 400m);
        Assert.Equal(1, await connection.ExecuteScalarAsync<int>(
            "SELECT count(*)::int FROM stock_movements WHERE item_id = @id AND direction = 'OUT'",
            new { id = item.Id }));
    }

    [Fact]
    public async Task Pos_direct_item_falls_back_to_a_warehouse_that_holds_the_stock()
    {
        var actor = await _db.AdminActorAsync();
        await _db.SeedAccountingAsync();
        var branch = await _db.CreateBranchAsync("DX-B4", "فرع مخزن بديل");
        var holding = await _db.CreateWarehouseAsync("DX-W4A", "مخزن الرصيد", branch);
        var empty = await _db.CreateWarehouseAsync("DX-W4B", "مخزن فارغ", branch);
        var item = await _db.CreateItemAsync("DX-IT4", "صنف مخزن بديل");
        await _db.PostReceiptAsync(item, branch, holding, 15, 30);
        await using (var setup = new NpgsqlConnection(TestDb.AdminConnection))
        {
            await setup.OpenAsync();
            await setup.ExecuteAsync(
                "UPDATE items SET execution_mode = 'direct' WHERE id = @id", new { id = item.Id });
        }
        var customer = await _db.PartnerRepo.CreateAsync(PartnerKind.Customer, new PartnerInput
        { Code = "DX-C4", NameAr = "عميل مخزن بديل" });

        // المستخدم اختار المخزن الفارغ: يجب أن يُصرف من المخزن الذي يحمل الرصيد.
        var receipt = await _db.Pos().CheckoutAsync(actor, new PosCheckoutInput
        {
            BranchId = branch, CustomerId = customer.Id, WarehouseId = empty,
            DocDate = DateTime.Today,
            Lines =
            {
                new PosLineInput
                {
                    ItemId = item.Id, UnitId = item.BaseUnitId, Quantity = 5, UnitPrice = 60
                }
            }
        });

        Assert.Equal(10m, await _db.OnHandAsync(item.Id, holding));
        Assert.Equal(0m, await _db.OnHandAsync(item.Id, empty));

        await using var connection = new NpgsqlConnection(TestDb.AdminConnection);
        await connection.OpenAsync();
        Assert.Equal(1, await connection.ExecuteScalarAsync<int>(
            "SELECT count(*)::int FROM stock_movements WHERE item_id = @id AND direction = 'OUT' " +
            "AND warehouse_id = @warehouse", new { id = item.Id, warehouse = holding }));
        Assert.Equal(BookingStatuses.Completed, await connection.ExecuteScalarAsync<string>(
            "SELECT status FROM bookings WHERE id = @id", new { id = receipt.BookingId }));
    }

    [Fact]
    public async Task Editing_an_item_keeps_its_direct_execution_mode_and_flags()
    {
        var actor = await _db.AdminActorAsync();
        var unit = await _db.UnitRepo.CreateAsync(new UnitInput
        { Code = "DX-U5", NameAr = "وحدة حفظ الإعدادات" });
        var item = await _db.ItemRepo.CreateAsync(new ItemInput
        {
            Code = "DX-IT5", NameAr = "صنف حفظ الإعدادات", BaseUnitId = unit.Id,
            ExecutionMode = ItemExecutionModes.Direct, IsConsumable = true,
            PriceIncludesTax = true, DefaultPrice = 25,
            Units = new List<ItemUnitInput> { new() { UnitId = unit.Id, ConversionFactor = 1 } }
        });

        var detail = (await _db.ItemRepo.GetDetailAsync(item.Id))!;
        Assert.Equal(ItemExecutionModes.Direct, detail.Item.ExecutionMode);

        // نفس ما تفعله الشاشة: تعيد بناء المدخل من التفاصيل ثم تحفظ.
        var input = new ItemInput
        {
            Code = detail.Item.Code, NameAr = detail.Item.NameAr, BaseUnitId = detail.Item.BaseUnitId,
            ItemType = detail.Item.ItemType, IsMetered = detail.Item.IsMetered,
            IsActive = detail.Item.IsActive, ExecutionMode = detail.Item.ExecutionMode,
            IsConsumable = detail.Item.IsConsumable, TrackExpiry = detail.Item.TrackExpiry,
            PriceIncludesTax = detail.Item.PriceIncludesTax, TaxId = detail.Item.TaxId,
            MeteredAssetId = detail.Item.MeteredAssetId, DefaultPrice = detail.Item.DefaultPrice,
            Units = detail.Units.Select(u => new ItemUnitInput
            { UnitId = u.UnitId, ConversionFactor = u.ConversionFactor }).ToList()
        };
        await _db.ItemService().UpdateAsync(actor, item.Id, item.Version, input);

        var reloaded = (await _db.ItemRepo.GetAsync(item.Id))!;
        Assert.Equal(ItemExecutionModes.Direct, reloaded.ExecutionMode);
        Assert.True(reloaded.IsConsumable);
        Assert.True(reloaded.PriceIncludesTax);
        Assert.Equal(25m, reloaded.DefaultPrice);
    }
}
