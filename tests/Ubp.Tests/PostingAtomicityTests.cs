using Dapper;
using Npgsql;
using Ubp.Core.Models;
using Ubp.Core.Results;
using Xunit;

namespace Ubp.Tests;

/// <summary>تراجع كامل عند فشل الترحيل في منتصفه (لا حركات ولا أرصدة ولا حالة نصفية).</summary>
public sealed class PostingAtomicityTests : IClassFixture<TestDb>
{
    private readonly TestDb _db;

    public PostingAtomicityTests(TestDb db) => _db = db;

    [Fact]
    public async Task Failed_posting_rolls_back_every_line()
    {
        var branch = await _db.CreateBranchAsync("AT-B", "فرع التراجع");
        var warehouse = await _db.CreateWarehouseAsync("AT-W", "مخزن التراجع", branch);
        var stocked = await _db.CreateItemAsync("IT-AT-A", "صنف له رصيد");
        var empty = await _db.CreateItemAsync("IT-AT-B", "صنف بلا رصيد");
        await _db.PostReceiptAsync(stocked, branch, warehouse, 10, 4);
        var actor = await _db.AdminActorAsync();

        var draft = await _db.StockDocRepo.CreateDraftAsync(new StockDocumentInput
        {
            DocType = StockDocumentTypes.Issue,
            DocDate = DateTime.UtcNow.Date,
            BranchId = branch,
            WarehouseId = warehouse,
            Lines =
            {
                new StockDocumentLineInput
                {
                    ItemId = stocked.Id, UnitId = stocked.BaseUnitId, ConversionFactor = 1, Quantity = 5
                },
                new StockDocumentLineInput
                {
                    ItemId = empty.Id, UnitId = empty.BaseUnitId, ConversionFactor = 1, Quantity = 1
                }
            }
        }, actor.UserId);

        var exception = await Assert.ThrowsAsync<ValidationAppException>(async () =>
            await _db.StockPostingRepo.PostAsync(draft.Id, actor.UserId, false));
        Assert.Equal("validation.negative_stock", exception.Code);

        Assert.Equal(10, await _db.OnHandAsync(stocked.Id, warehouse));
        await using var connection = new NpgsqlConnection(TestDb.AdminConnection);
        await connection.OpenAsync();
        Assert.Equal(0, await connection.ExecuteScalarAsync<int>(
            "SELECT count(*)::int FROM stock_movements WHERE document_id = @id", new { id = draft.Id }));
        Assert.Equal(StockStatuses.Draft, await connection.ExecuteScalarAsync<string>(
            "SELECT status FROM stock_documents WHERE id = @id", new { id = draft.Id }));
    }
}
