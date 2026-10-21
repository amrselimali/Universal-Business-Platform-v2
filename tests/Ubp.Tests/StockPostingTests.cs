using Dapper;
using Npgsql;
using Ubp.Core.Models;
using Ubp.Core.Results;
using Xunit;

namespace Ubp.Tests;

public sealed class StockPostingTests : IClassFixture<TestDb>
{
    private readonly TestDb _db;

    public StockPostingTests(TestDb db) => _db = db;

    private async Task<(Guid Branch, Guid Warehouse)> NewWarehouseAsync(string suffix,
        Guid? branch = null)
    {
        var branchId = branch ?? await _db.CreateBranchAsync("B-" + suffix, "فرع " + suffix);
        var warehouseId = await _db.CreateWarehouseAsync("W-" + suffix, "مخزن " + suffix, branchId);
        return (branchId, warehouseId);
    }

    private async Task<StockDocument> PostIssueAsync(Item item, Guid branchId, Guid warehouseId,
        decimal quantity, bool allowNegative = false)
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
                    ItemId = item.Id, UnitId = item.BaseUnitId, ConversionFactor = 1,
                    Quantity = quantity
                }
            }
        }, actor.UserId);
        return await _db.StockPostingRepo.PostAsync(draft.Id, actor.UserId, allowNegative);
    }

    [Fact]
    public async Task Receipt_posts_movement_and_uses_moving_weighted_average()
    {
        var (branch, warehouse) = await NewWarehouseAsync("AVG");
        var item = await _db.CreateItemAsync("IT-AVG", "صنف المتوسط");

        await _db.PostReceiptAsync(item, branch, warehouse, 10, 5);
        await _db.PostReceiptAsync(item, branch, warehouse, 10, 15);

        var balance = await _db.StockReportRepo.ListBalancesAsync(
            new Ubp.Core.Paging.PageRequest(), warehouse, false, true, Array.Empty<Guid>());
        var row = Assert.Single(balance.Items);
        Assert.Equal(20, row.OnHand);
        Assert.Equal(10, row.AverageCost);
        Assert.Equal(200, row.InventoryValue);
    }

    [Fact]
    public async Task Issue_costs_at_current_average_and_keeps_balance_value_consistent()
    {
        var (branch, warehouse) = await NewWarehouseAsync("ISS");
        var item = await _db.CreateItemAsync("IT-ISS", "صنف الصرف");
        await _db.PostReceiptAsync(item, branch, warehouse, 10, 5);
        await _db.PostReceiptAsync(item, branch, warehouse, 10, 15);

        var issue = await PostIssueAsync(item, branch, warehouse, 5);

        Assert.Equal(StockStatuses.Posted, issue.Status);
        Assert.Equal(15, await _db.OnHandAsync(item.Id, warehouse));
        await using var connection = new NpgsqlConnection(TestDb.AdminConnection);
        await connection.OpenAsync();
        var inventoryValue = await connection.ExecuteScalarAsync<decimal>(
            "SELECT inventory_value FROM stock_balances WHERE item_id=@item AND warehouse_id=@wh",
            new { item = item.Id, wh = warehouse });
        Assert.Equal(150, inventoryValue);
        var movementCost = await connection.ExecuteScalarAsync<decimal>(
            "SELECT total_cost FROM stock_movements WHERE document_id=@doc AND direction='OUT'",
            new { doc = issue.Id });
        Assert.Equal(50, movementCost);
    }

    [Fact]
    public async Task Negative_stock_is_rejected_by_default_and_allowed_when_enabled()
    {
        var (branch, warehouse) = await NewWarehouseAsync("NEG");
        var item = await _db.CreateItemAsync("IT-NEG", "صنف سالب");

        var exception = await Assert.ThrowsAsync<ValidationAppException>(async () =>
            await PostIssueAsync(item, branch, warehouse, 1));
        Assert.Equal("validation.negative_stock", exception.Code);
        Assert.Equal(0, await _db.OnHandAsync(item.Id, warehouse));

        await PostIssueAsync(item, branch, warehouse, 1, allowNegative: true);
        Assert.Equal(-1, await _db.OnHandAsync(item.Id, warehouse));
    }

    [Fact]
    public async Task Posting_into_a_closed_period_is_rejected()
    {
        var (branch, warehouse) = await NewWarehouseAsync("PRD");
        var item = await _db.CreateItemAsync("IT-PRD", "صنف الفترة");
        var actor = await _db.AdminActorAsync();

        var period = await _db.PeriodRepo.CreateAsync(new FiscalPeriodInput
        {
            Code = "P-" + Guid.NewGuid().ToString("N")[..6],
            NameAr = "فترة اختبار",
            StartDate = new DateTime(2031, 1, 1),
            EndDate = new DateTime(2031, 1, 31)
        });
        await _db.PeriodRepo.UpsertClosureAsync(period.Id, PeriodScopes.Company, null,
            PeriodModules.Inventory, true, actor.UserId, null);

        var draft = await _db.StockDocRepo.CreateDraftAsync(new StockDocumentInput
        {
            DocType = StockDocumentTypes.Receipt,
            DocDate = new DateTime(2031, 1, 15),
            BranchId = branch,
            WarehouseId = warehouse,
            Lines =
            {
                new StockDocumentLineInput
                {
                    ItemId = item.Id, UnitId = item.BaseUnitId, ConversionFactor = 1,
                    Quantity = 5, UnitCost = 2
                }
            }
        }, actor.UserId);

        var exception = await Assert.ThrowsAsync<ValidationAppException>(async () =>
            await _db.StockPostingRepo.PostAsync(draft.Id, actor.UserId, false));
        Assert.Equal("validation.period_closed", exception.Code);
    }

    [Fact]
    public async Task Reversal_creates_opposite_movements_and_cannot_be_repeated()
    {
        var (branch, warehouse) = await NewWarehouseAsync("REV");
        var item = await _db.CreateItemAsync("IT-REV", "صنف العكس");
        var receipt = await _db.PostReceiptAsync(item, branch, warehouse, 10, 5);
        var actor = await _db.AdminActorAsync();

        var reversal = await _db.StockPostingRepo.ReverseAsync(receipt.Id, actor.UserId, false);
        Assert.Equal(StockDocumentTypes.Reversal, reversal.DocType);
        Assert.Equal(receipt.Id, reversal.ReversalOfId);
        Assert.Equal(0, await _db.OnHandAsync(item.Id, warehouse));

        var exception = await Assert.ThrowsAsync<ValidationAppException>(async () =>
            await _db.StockPostingRepo.ReverseAsync(receipt.Id, actor.UserId, false));
        Assert.Equal("validation.document_already_reversed", exception.Code);
    }

    [Fact]
    public async Task Transfer_requires_receipt_before_destination_balance_changes()
    {
        var (branchA, warehouseA) = await NewWarehouseAsync("TRA");
        var (branchB, warehouseB) = await NewWarehouseAsync("TRB");
        var item = await _db.CreateItemAsync("IT-TRA", "صنف التحويل");
        await _db.PostReceiptAsync(item, branchA, warehouseA, 10, 6);
        var actor = await _db.AdminActorAsync();

        var draft = await _db.StockDocRepo.CreateDraftAsync(new StockDocumentInput
        {
            DocType = StockDocumentTypes.Transfer,
            DocDate = DateTime.UtcNow.Date,
            BranchId = branchA,
            WarehouseId = warehouseA,
            ToBranchId = branchB,
            ToWarehouseId = warehouseB,
            Lines =
            {
                new StockDocumentLineInput
                {
                    ItemId = item.Id, UnitId = item.BaseUnitId, ConversionFactor = 1, Quantity = 10
                }
            }
        }, actor.UserId);
        await _db.StockPostingRepo.PostAsync(draft.Id, actor.UserId, false);

        Assert.Equal(0, await _db.OnHandAsync(item.Id, warehouseA));
        Assert.Equal(0, await _db.OnHandAsync(item.Id, warehouseB));

        var detail = await _db.StockDocRepo.GetDetailAsync(draft.Id);
        var line = Assert.Single(detail!.Lines);

        await _db.StockPostingRepo.ReceiveTransferAsync(new TransferReceiptInput
        {
            DocumentId = draft.Id,
            Lines = { new TransferReceiptLineInput { DocumentLineId = line.Id, Quantity = 4 } }
        }, actor.UserId, false);
        Assert.Equal(4, await _db.OnHandAsync(item.Id, warehouseB));
        var partial = await _db.StockDocRepo.GetAsync(draft.Id);
        Assert.Equal(StockStatuses.PartiallyReceived, partial!.Status);

        var over = await Assert.ThrowsAsync<ValidationAppException>(async () =>
            await _db.StockPostingRepo.ReceiveTransferAsync(new TransferReceiptInput
            {
                DocumentId = draft.Id,
                Lines = { new TransferReceiptLineInput { DocumentLineId = line.Id, Quantity = 7 } }
            }, actor.UserId, false));
        Assert.Equal("validation.receipt_exceeds_remaining", over.Code);

        await _db.StockPostingRepo.ReceiveTransferAsync(new TransferReceiptInput
        {
            DocumentId = draft.Id,
            Lines = { new TransferReceiptLineInput { DocumentLineId = line.Id, Quantity = 6 } }
        }, actor.UserId, false);
        Assert.Equal(10, await _db.OnHandAsync(item.Id, warehouseB));
        var received = await _db.StockDocRepo.GetAsync(draft.Id);
        Assert.Equal(StockStatuses.Received, received!.Status);

        var exception = await Assert.ThrowsAsync<ValidationAppException>(async () =>
            await _db.StockPostingRepo.ReceiveTransferAsync(new TransferReceiptInput
            {
                DocumentId = draft.Id,
                Lines = { new TransferReceiptLineInput { DocumentLineId = line.Id, Quantity = 1 } }
            }, actor.UserId, false));
        Assert.Equal("validation.transfer_not_receivable", exception.Code);
    }

    [Fact]
    public async Task Posted_document_cannot_be_edited_or_deleted()
    {
        var (branch, warehouse) = await NewWarehouseAsync("LCK");
        var item = await _db.CreateItemAsync("IT-LCK", "صنف القفل");
        var receipt = await _db.PostReceiptAsync(item, branch, warehouse, 5, 3);

        await using var connection = new NpgsqlConnection(TestDb.AdminConnection);
        await connection.OpenAsync();
        var update = await Assert.ThrowsAsync<PostgresException>(async () =>
            await connection.ExecuteAsync("UPDATE stock_documents SET doc_date = doc_date - 1 WHERE id=@id",
                new { id = receipt.Id }));
        Assert.Equal("23514", update.SqlState);

        var delete = await Assert.ThrowsAsync<PostgresException>(async () =>
            await connection.ExecuteAsync("DELETE FROM stock_documents WHERE id=@id", new { id = receipt.Id }));
        Assert.Equal("23514", delete.SqlState);
    }

    [Fact]
    public async Task Balances_always_match_the_sum_of_movements()
    {
        var (branch, warehouse) = await NewWarehouseAsync("BAL");
        var item = await _db.CreateItemAsync("IT-BAL", "صنف المطابقة");
        await _db.PostReceiptAsync(item, branch, warehouse, 10, 4);
        await PostIssueAsync(item, branch, warehouse, 3);

        await using var connection = new NpgsqlConnection(TestDb.AdminConnection);
        await connection.OpenAsync();
        var mismatches = await connection.ExecuteScalarAsync<int>("""
            SELECT count(*)::int FROM stock_balances b
            WHERE b.on_hand <> COALESCE((
                SELECT sum(CASE WHEN m.direction='IN' THEN m.quantity ELSE -m.quantity END)
                FROM stock_movements m
                WHERE m.item_id=b.item_id AND m.warehouse_id=b.warehouse_id), 0)
            """);
        Assert.Equal(0, mismatches);
    }
}
