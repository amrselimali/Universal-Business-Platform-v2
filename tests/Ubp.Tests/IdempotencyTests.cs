using Dapper;
using Npgsql;
using Ubp.Core.Models;
using Xunit;

namespace Ubp.Tests;

/// <summary>
/// مفتاح idempotency للمستندات المخزنية (الترحيل 0038): إعادة المحاولة لا تُنشئ مستنداً ثانياً،
/// حتى عند وصول الطلبات في نفس اللحظة.
/// </summary>
public sealed class IdempotencyTests : IClassFixture<TestDb>
{
    private readonly TestDb _db;

    public IdempotencyTests(TestDb db) => _db = db;

    private StockDocumentInput NewInput(Guid branch, Guid warehouse, Item item, string? key) =>
        new()
        {
            DocType = StockDocumentTypes.Receipt,
            DocDate = DateTime.UtcNow.Date,
            BranchId = branch,
            WarehouseId = warehouse,
            IdempotencyKey = key,
            Lines =
            {
                new StockDocumentLineInput
                {
                    ItemId = item.Id, UnitId = item.BaseUnitId, ConversionFactor = 1,
                    Quantity = 3, UnitCost = 4
                }
            }
        };

    private async Task<int> CountAsync(Guid branch, string key)
    {
        await using var connection = new NpgsqlConnection(TestDb.AdminConnection);
        await connection.OpenAsync();
        return await connection.ExecuteScalarAsync<int>(
            "SELECT count(*)::int FROM stock_documents WHERE branch_id = @branch AND idempotency_key = @key",
            new { branch, key });
    }

    [Fact]
    public async Task Replaying_the_same_key_returns_the_same_document()
    {
        var branch = await _db.CreateBranchAsync("IDEM-A", "فرع التكرار");
        var warehouse = await _db.CreateWarehouseAsync("IDEM-WA", "مخزن التكرار", branch);
        var item = await _db.CreateItemAsync("IT-IDEM-A", "صنف التكرار");
        var actor = await _db.AdminActorAsync();

        var first = await _db.StockDocRepo.CreateDraftAsync(
            NewInput(branch, warehouse, item, "retry-1"), actor.UserId);
        var second = await _db.StockDocRepo.CreateDraftAsync(
            NewInput(branch, warehouse, item, "retry-1"), actor.UserId);

        Assert.Equal(first.Id, second.Id);
        Assert.Equal(1, await CountAsync(branch, "retry-1"));
    }

    [Fact]
    public async Task Concurrent_retries_create_a_single_document()
    {
        var branch = await _db.CreateBranchAsync("IDEM-B", "فرع التزامن");
        var warehouse = await _db.CreateWarehouseAsync("IDEM-WB", "مخزن التزامن", branch);
        var item = await _db.CreateItemAsync("IT-IDEM-B", "صنف تزامن المفتاح");
        var actor = await _db.AdminActorAsync();

        var ids = await Task.WhenAll(Enumerable.Range(0, 10).Select(_ =>
            _db.StockDocRepo.CreateDraftAsync(NewInput(branch, warehouse, item, "retry-race"),
                actor.UserId)));

        Assert.Single(ids.Select(d => d.Id).Distinct());
        Assert.Equal(1, await CountAsync(branch, "retry-race"));
    }

    [Fact]
    public async Task Different_keys_create_different_documents()
    {
        var branch = await _db.CreateBranchAsync("IDEM-C", "فرع مفاتيح");
        var warehouse = await _db.CreateWarehouseAsync("IDEM-WC", "مخزن مفاتيح", branch);
        var item = await _db.CreateItemAsync("IT-IDEM-C", "صنف مفاتيح");
        var actor = await _db.AdminActorAsync();

        var first = await _db.StockDocRepo.CreateDraftAsync(
            NewInput(branch, warehouse, item, "key-a"), actor.UserId);
        var second = await _db.StockDocRepo.CreateDraftAsync(
            NewInput(branch, warehouse, item, "key-b"), actor.UserId);

        Assert.NotEqual(first.Id, second.Id);
        await using var connection = new NpgsqlConnection(TestDb.AdminConnection);
        await connection.OpenAsync();
        Assert.Equal(2, await connection.ExecuteScalarAsync<int>(
            "SELECT count(*)::int FROM stock_documents WHERE branch_id = @branch AND idempotency_key IS NOT NULL",
            new { branch }));
    }

    [Fact]
    public async Task Documents_without_a_key_are_never_deduplicated()
    {
        var branch = await _db.CreateBranchAsync("IDEM-D", "فرع بلا مفتاح");
        var warehouse = await _db.CreateWarehouseAsync("IDEM-WD", "مخزن بلا مفتاح", branch);
        var item = await _db.CreateItemAsync("IT-IDEM-D", "صنف بلا مفتاح");
        var actor = await _db.AdminActorAsync();

        var first = await _db.StockDocRepo.CreateDraftAsync(
            NewInput(branch, warehouse, item, null), actor.UserId);
        var second = await _db.StockDocRepo.CreateDraftAsync(
            NewInput(branch, warehouse, item, null), actor.UserId);

        Assert.NotEqual(first.Id, second.Id);
    }
}
