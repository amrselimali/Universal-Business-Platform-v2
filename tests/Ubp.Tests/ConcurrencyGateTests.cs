using Dapper;
using Npgsql;
using Ubp.Core.Models;
using Ubp.Core.Results;
using Ubp.Infrastructure.Repositories;
using Xunit;

namespace Ubp.Tests;

/// <summary>
/// بوابة التزامن (المرحلة 3): ترحيل متزامن، أوزان الأرقام تحت الحمل، وتراجع كامل عند الفشل.
/// </summary>
public sealed class ConcurrencyGateTests : IClassFixture<TestDb>
{
    private readonly TestDb _db;

    public ConcurrencyGateTests(TestDb db) => _db = db;

    private static async Task<string> OutcomeAsync(Task task)
    {
        try { await task; return "ok"; }
        catch (ValidationAppException ex) { return ex.Code; }
        catch (ConflictException ex) { return ex.Code; }
        catch (Exception ex) { return ex.GetType().Name; }
    }

    private async Task<StockDocument> NewReceiptAsync(Item item, Guid branch, Guid warehouse,
        decimal quantity, decimal cost)
    {
        var actor = await _db.AdminActorAsync();
        return await _db.StockDocRepo.CreateDraftAsync(new StockDocumentInput
        {
            DocType = StockDocumentTypes.Receipt,
            DocDate = DateTime.UtcNow.Date,
            BranchId = branch,
            WarehouseId = warehouse,
            Lines =
            {
                new StockDocumentLineInput
                {
                    ItemId = item.Id, UnitId = item.BaseUnitId, ConversionFactor = 1,
                    Quantity = quantity, UnitCost = cost
                }
            }
        }, actor.UserId);
    }

    [Fact]
    public async Task Concurrent_posting_of_the_same_document_succeeds_exactly_once()
    {
        var branch = await _db.CreateBranchAsync("CG-P1", "فرع ترحيل متزامن");
        var warehouse = await _db.CreateWarehouseAsync("CG-W1", "مخزن ترحيل متزامن", branch);
        var item = await _db.CreateItemAsync("IT-CG1", "صنف ترحيل متزامن");
        var actor = await _db.AdminActorAsync();
        var draft = await NewReceiptAsync(item, branch, warehouse, 5, 2);

        var outcomes = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ =>
            OutcomeAsync(_db.StockPostingRepo.PostAsync(draft.Id, actor.UserId, false))));

        Assert.Equal(1, outcomes.Count(o => o == "ok"));
        Assert.All(outcomes.Where(o => o != "ok"),
            o => Assert.Equal("validation.document_not_draft", o));

        await using var connection = new NpgsqlConnection(TestDb.AdminConnection);
        await connection.OpenAsync();
        var movements = await connection.ExecuteScalarAsync<int>(
            "SELECT count(*)::int FROM stock_movements WHERE document_id = @id", new { id = draft.Id });
        Assert.Equal(1, movements);
        Assert.Equal(5, await _db.OnHandAsync(item.Id, warehouse));
    }

    [Fact]
    public async Task Fifty_concurrent_receipts_keep_numbers_and_balances_consistent()
    {
        await using var isolated = await IsolatedDb.CreateAsync("conc");
        var (branch, warehouse) = await isolated.CreateBranchWithWarehouseAsync("C");
        var item = await isolated.CreateItemAsync("IT-CONC", "صنف خمسين");
        var admin = await isolated.CreateAdminAsync();

        var docs = new StockDocumentRepository(isolated.Factory);
        var posting = new StockPostingRepository(isolated.Factory);

        var ids = await Task.WhenAll(Enumerable.Range(0, 50).Select(async index =>
        {
            var draft = await docs.CreateDraftAsync(new StockDocumentInput
            {
                DocType = StockDocumentTypes.Receipt,
                DocDate = DateTime.UtcNow.Date,
                BranchId = branch,
                WarehouseId = warehouse,
                IdempotencyKey = "conc-" + index,
                Lines =
                {
                    new StockDocumentLineInput
                    {
                        ItemId = item.Id, UnitId = item.BaseUnitId, ConversionFactor = 1,
                        Quantity = 1, UnitCost = 10
                    }
                }
            }, admin);
            await posting.PostAsync(draft.Id, admin, false);
            return draft.Id;
        }));

        Assert.Equal(50, ids.Distinct().Count());
        Assert.Equal(50, await isolated.ScalarAsync(
            "SELECT count(DISTINCT doc_no)::int FROM stock_documents WHERE status = 'POSTED'"));
        Assert.Equal(50, await isolated.ScalarAsync(
            "SELECT on_hand::int FROM stock_balances WHERE item_id = @item AND warehouse_id = @wh",
            new { item = item.Id, wh = warehouse }));
        Assert.Equal(500, await isolated.ScalarAsync(
            "SELECT inventory_value::int FROM stock_balances WHERE item_id = @item AND warehouse_id = @wh",
            new { item = item.Id, wh = warehouse }));
    }
}
