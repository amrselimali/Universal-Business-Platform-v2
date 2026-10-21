using Ubp.Core.Models;
using Ubp.Core.Results;
using Xunit;

namespace Ubp.Tests;

public sealed class ClosingChecksTests : IClassFixture<TestDb>
{
    private readonly TestDb _db;

    public ClosingChecksTests(TestDb db) => _db = db;

    private async Task<FiscalPeriod> NewPeriodAsync(string suffix)
    {
        var today = DateTime.UtcNow.Date;
        var existing = (await _db.PeriodRepo.ListOptionsAsync())
            .FirstOrDefault(p => p.StartDate.Date <= today && p.EndDate.Date >= today);
        if (existing is not null) return existing;

        var actor = await _db.AdminActorAsync();
        return await _db.PeriodService().CreateAsync(actor, new FiscalPeriodInput
        {
            Code = "CHK-" + suffix + "-" + Guid.NewGuid().ToString("N")[..5],
            NameAr = "فترة فحص " + suffix,
            StartDate = today.AddDays(-1),
            EndDate = today.AddDays(1)
        });
    }

    private async Task AssertBlockedAsync(FiscalPeriod period, Guid branch, string checkCode)
    {
        var actor = await _db.AdminActorAsync();
        var exception = await Assert.ThrowsAsync<ValidationAppException>(async () =>
            await _db.PeriodService().CloseAsync(actor, period.Id, PeriodScopes.Branch, branch,
                PeriodModules.Inventory));
        Assert.Equal("validation.period_close_blocked", exception.Code);
        Assert.Equal(checkCode, exception.Args["check"]);
    }

    [Fact]
    public async Task Unfinished_transfer_blocks_period_close()
    {
        var branchA = await _db.CreateBranchAsync("CHK-TRA", "فرع تحويل معلق");
        var warehouseA = await _db.CreateWarehouseAsync("CHK-TWA", "مخزن أ", branchA);
        var branchB = await _db.CreateBranchAsync("CHK-TRB", "فرع وجهة");
        var warehouseB = await _db.CreateWarehouseAsync("CHK-TWB", "مخزن ب", branchB);
        var item = await _db.CreateItemAsync("IT-CHK-TR", "صنف تحويل معلق");
        var actor = await _db.AdminActorAsync();

        await _db.PostReceiptAsync(item, branchA, warehouseA, 10, 5);
        var draft = await _db.StockDocRepo.CreateDraftAsync(new StockDocumentInput
        {
            DocType = StockDocumentTypes.Transfer, DocDate = DateTime.UtcNow.Date,
            BranchId = branchA, WarehouseId = warehouseA,
            ToBranchId = branchB, ToWarehouseId = warehouseB,
            Lines =
            {
                new StockDocumentLineInput
                {
                    ItemId = item.Id, UnitId = item.BaseUnitId, ConversionFactor = 1, Quantity = 4
                }
            }
        }, actor.UserId);
        await _db.StockPostingRepo.PostAsync(draft.Id, actor.UserId, false);

        var period = await NewPeriodAsync("TR");
        await AssertBlockedAsync(period, branchA, "UNFINISHED_TRANSFERS");
    }

    [Fact]
    public async Task Draft_stocktake_blocks_period_close()
    {
        var branch = await _db.CreateBranchAsync("CHK-STK", "فرع جرد معلق");
        var warehouse = await _db.CreateWarehouseAsync("CHK-STW", "مخزن جرد معلق", branch);
        var actor = await _db.AdminActorAsync();
        var code = await _db.StocktakeRepo.NextCodeAsync(branch, DateTime.UtcNow.Date);
        await _db.StocktakeRepo.CreateAsync(new StocktakeInput
        {
            BranchId = branch, WarehouseId = warehouse, Notes = "جرد لم يُرحّل"
        }, code, actor.UserId);

        var period = await NewPeriodAsync("ST");
        await AssertBlockedAsync(period, branch, "UNPOSTED_STOCKTAKES");
    }

    [Fact]
    public async Task Zero_cost_balance_blocks_period_close()
    {
        var branch = await _db.CreateBranchAsync("CHK-ZERO", "فرع تكلفة صفر");
        var warehouse = await _db.CreateWarehouseAsync("CHK-ZEW", "مخزن تكلفة صفر", branch);
        var item = await _db.CreateItemAsync("IT-CHK-ZERO", "صنف بلا تكلفة");

        await _db.PostReceiptAsync(item, branch, warehouse, 5, 0);
        Assert.Equal(5, await _db.OnHandAsync(item.Id, warehouse));

        var period = await NewPeriodAsync("ZE");
        await AssertBlockedAsync(period, branch, "ZERO_COST_BALANCES");
    }
}
