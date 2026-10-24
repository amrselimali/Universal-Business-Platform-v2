using Dapper;
using Npgsql;
using Ubp.Core.Models;
using Ubp.Core.Results;
using Xunit;

namespace Ubp.Tests;

public sealed class StockLotTests : IClassFixture<TestDb>
{
    private readonly TestDb _db;

    public StockLotTests(TestDb db) => _db = db;

    private async Task<(Guid Branch, Guid Warehouse)> PlaceAsync(string suffix)
    {
        var branch = await _db.CreateBranchAsync("LOT-B-" + suffix, "فرع " + suffix);
        var warehouse = await _db.CreateWarehouseAsync("LOT-W-" + suffix, "مخزن " + suffix, branch);
        return (branch, warehouse);
    }

    private async Task<Item> TrackedItemAsync(string code)
    {
        var unit = await _db.UnitRepo.CreateAsync(new UnitInput
        { Code = "U-" + code, NameAr = "وحدة " + code });
        return await _db.ItemRepo.CreateAsync(new ItemInput
        {
            Code = code, NameAr = "صنف متتبَّع " + code, BaseUnitId = unit.Id, TrackExpiry = true,
            Units = { new ItemUnitInput { UnitId = unit.Id, ConversionFactor = 1 } }
        });
    }

    private async Task<decimal> LotQuantityAsync(Guid itemId, Guid warehouseId, string lotNumber)
    {
        await using var connection = new NpgsqlConnection(TestDb.AdminConnection);
        await connection.OpenAsync();
        return await connection.ExecuteScalarAsync<decimal?>("""
            SELECT quantity_on_hand FROM stock_lots
            WHERE item_id = @itemId AND warehouse_id = @warehouseId AND lot_number = @lotNumber
            """, new { itemId, warehouseId, lotNumber }) ?? 0;
    }

    private async Task PostIssueAsync(Item item, Guid branchId, Guid warehouseId, decimal quantity)
    {
        var actor = await _db.AdminActorAsync();
        var draft = await _db.StockDocRepo.CreateDraftAsync(new StockDocumentInput
        {
            DocType = StockDocumentTypes.Issue,
            DocDate = DateTime.UtcNow.Date,
            BranchId = branchId,
            WarehouseId = warehouseId,
            Lines =
            {
                new StockDocumentLineInput
                {
                    ItemId = item.Id, UnitId = item.BaseUnitId, ConversionFactor = 1, Quantity = quantity
                }
            }
        }, actor.UserId);
        await _db.StockPostingRepo.PostAsync(draft.Id, actor.UserId, false);
    }

    [Fact]
    public async Task Receipt_creates_a_lot_and_keeps_lots_equal_to_on_hand()
    {
        var (branch, warehouse) = await PlaceAsync("R1");
        var item = await TrackedItemAsync("IT-LOT-R1");

        await _db.PostReceiptWithLotAsync(item, branch, warehouse, 10, 5, "L-001",
            DateTime.Today.AddMonths(6));

        Assert.Equal(10m, await LotQuantityAsync(item.Id, warehouse, "L-001"));
        Assert.Equal(10m, await _db.OnHandAsync(item.Id, warehouse));

        await using var connection = new NpgsqlConnection(TestDb.AdminConnection);
        await connection.OpenAsync();
        var mismatches = await connection.ExecuteScalarAsync<int>("""
            SELECT count(*)::int FROM stock_balances b
            WHERE b.on_hand <> COALESCE((SELECT sum(l.quantity_on_hand) FROM stock_lots l
                WHERE l.item_id = b.item_id AND l.warehouse_id = b.warehouse_id), 0)
            """);
        Assert.Equal(0, mismatches);
    }

    [Fact]
    public async Task Issue_consumes_the_nearest_expiry_lot_first()
    {
        var (branch, warehouse) = await PlaceAsync("FEFO");
        var item = await TrackedItemAsync("IT-LOT-FEFO");

        await _db.PostReceiptWithLotAsync(item, branch, warehouse, 10, 5, "LATE",
            DateTime.Today.AddMonths(12));
        await _db.PostReceiptWithLotAsync(item, branch, warehouse, 10, 7, "SOON",
            DateTime.Today.AddMonths(2));

        await PostIssueAsync(item, branch, warehouse, 4);

        Assert.Equal(6m, await LotQuantityAsync(item.Id, warehouse, "SOON"));
        Assert.Equal(10m, await LotQuantityAsync(item.Id, warehouse, "LATE"));
        Assert.Equal(16m, await _db.OnHandAsync(item.Id, warehouse));

        await PostIssueAsync(item, branch, warehouse, 8);
        Assert.Equal(0m, await LotQuantityAsync(item.Id, warehouse, "SOON"));
        Assert.Equal(8m, await LotQuantityAsync(item.Id, warehouse, "LATE"));
    }

    [Fact]
    public async Task Expired_lots_are_blocked_by_default_and_allowed_when_setting_is_off()
    {
        var (branch, warehouse) = await PlaceAsync("EXP");
        var item = await TrackedItemAsync("IT-LOT-EXP");
        await _db.PostReceiptWithLotAsync(item, branch, warehouse, 5, 3, "OLD",
            DateTime.Today.AddDays(-10));

        var blocked = await Assert.ThrowsAsync<ValidationAppException>(async () =>
            await PostIssueAsync(item, branch, warehouse, 1));
        Assert.Equal("validation.expired_lot", blocked.Code);

        await using (var connection = new NpgsqlConnection(TestDb.AdminConnection))
        {
            await connection.OpenAsync();
            await connection.ExecuteAsync("UPDATE app_settings SET block_expired_issue = false");
        }
        _db.Settings().Invalidate();

        await PostIssueAsync(item, branch, warehouse, 1);
        Assert.Equal(4m, await LotQuantityAsync(item.Id, warehouse, "OLD"));
    }

    [Fact]
    public async Task Tracked_item_can_never_go_negative()
    {
        var (branch, warehouse) = await PlaceAsync("NEG");
        var item = await TrackedItemAsync("IT-LOT-NEG");
        await _db.PostReceiptWithLotAsync(item, branch, warehouse, 2, 4, "ONLY",
            DateTime.Today.AddMonths(3));

        var exception = await Assert.ThrowsAsync<ValidationAppException>(async () =>
            await PostIssueAsync(item, branch, warehouse, 3));
        Assert.Equal("validation.negative_stock", exception.Code);
        Assert.Equal(2m, await LotQuantityAsync(item.Id, warehouse, "ONLY"));
    }

    [Fact]
    public async Task Non_tracked_items_stay_on_the_average_cost_path_without_lots()
    {
        var (branch, warehouse) = await PlaceAsync("AVG");
        var item = await _db.CreateItemAsync("IT-LOT-AVG", "صنف غير متتبَّع");

        await _db.PostReceiptAsync(item, branch, warehouse, 10, 5);
        await _db.PostReceiptAsync(item, branch, warehouse, 10, 15);

        Assert.Equal(20m, await _db.OnHandAsync(item.Id, warehouse));
        await using var connection = new NpgsqlConnection(TestDb.AdminConnection);
        await connection.OpenAsync();
        var lots = await connection.ExecuteScalarAsync<int>(
            "SELECT count(*)::int FROM stock_lots WHERE item_id = @id", new { id = item.Id });
        Assert.Equal(0, lots);
    }
}

