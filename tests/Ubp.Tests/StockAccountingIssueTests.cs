using Ubp.Core.Models;
using Ubp.Core.Results;
using Xunit;

namespace Ubp.Tests;

public sealed class StockAccountingIssueTests : IClassFixture<TestDb>
{
    private readonly TestDb _db;
    private static DateTime Date => new(2026, 8, 12);

    public StockAccountingIssueTests(TestDb db) => _db = db;

    [Fact]
    public async Task Issue_uses_cogs_account_and_matches_inventory_value()
    {
        var actor = await _db.AdminActorAsync();
        var chart = await _db.SeedAccountingAsync();
        var branch = await _db.CreateBranchAsync("SA-ISS", "فرع الصرف");
        var warehouse = await _db.CreateWarehouseAsync("SAW-ISS", "مخزن الصرف", branch);

        var category = await _db.ItemCategoryRepo.CreateAsync(new ItemCategoryInput
        {
            Code = "CAT-ISS", NameAr = "تصنيف الصرف"
        });
        var unit = await _db.UnitRepo.CreateAsync(new UnitInput { Code = "U-ISS", NameAr = "وحدة" });
        var item = await _db.ItemRepo.CreateAsync(new ItemInput
        {
            Code = "IT-ISS", NameAr = "صنف الصرف", CategoryId = category.Id, BaseUnitId = unit.Id,
            Units = { new ItemUnitInput { UnitId = unit.Id, ConversionFactor = 1 } }
        });
        await _db.AccountingSettings().SaveCategoryAccountAsync(actor, new ItemCategoryAccountInput
        {
            CategoryId = category.Id, InventoryAccountId = chart["1120"].Id,
            CogsAccountId = chart["5100"].Id
        });

        var receipt = await _db.PostReceiptAsync(item, branch, warehouse, 10, 5);
        await _db.StockAccountingService().GenerateAsync(actor, receipt.Id);

        var draft = await _db.StockDocumentService().SaveAsync(actor, null, 0, new StockDocumentInput
        {
            DocType = StockDocumentTypes.Issue,
            DocDate = Date,
            BranchId = branch,
            WarehouseId = warehouse,
            Lines =
            {
                new StockDocumentLineInput
                {
                    ItemId = item.Id, UnitId = item.BaseUnitId, ConversionFactor = 1, Quantity = 4
                }
            }
        });
        var postedIssue = await _db.StockDocumentService().PostAsync(actor, draft.Document.Id);
        await _db.StockAccountingService().GenerateAsync(actor, postedIssue.Document.Id);

        var reports = await _db.AccountingReports().StockVsGlAsync(actor);
        var row = Assert.Single(reports, r => r.StockValue != 0 || r.GlBalance != 0);
        Assert.Equal(30m, row.StockValue);
        Assert.Equal(30m, row.GlBalance);
        Assert.True(row.IsMatched);
    }

    [Fact]
    public async Task Missing_category_accounts_is_reported_and_nothing_is_posted()
    {
        var actor = await _db.AdminActorAsync();
        await _db.SeedAccountingAsync();
        var branch = await _db.CreateBranchAsync("SA-NONE", "فرع بلا حسابات");
        var warehouse = await _db.CreateWarehouseAsync("SAW-NONE", "مخزن بلا حسابات", branch);
        var item = await _db.CreateItemAsync("IT-NONE", "صنف بلا تصنيف");
        var receipt = await _db.PostReceiptAsync(item, branch, warehouse, 3, 7);

        var preview = await _db.StockAccountingService().PreviewAsync(actor, receipt.Id);
        Assert.False(preview.CanPost);
        Assert.Equal("validation.stock_accounts_missing", preview.ErrorKey);

        var exception = await Assert.ThrowsAsync<ValidationAppException>(async () =>
            await _db.StockAccountingService().GenerateAsync(actor, receipt.Id));
        Assert.Equal("validation.stock_accounts_missing", exception.Code);

        var entry = await _db.JournalEntryRepo.GetBySourceAsync(
            EntrySources.StockDocument, receipt.Id);
        Assert.Null(entry);
    }
}
