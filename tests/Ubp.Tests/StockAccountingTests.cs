using Ubp.Core.Models;
using Xunit;

namespace Ubp.Tests;

public sealed class StockAccountingTests : IClassFixture<TestDb>
{
    private readonly TestDb _db;
    private static DateTime Date => new(2026, 7, 10);

    public StockAccountingTests(TestDb db) => _db = db;

    private async Task<(Item Item, Guid Branch, Guid Warehouse)> PrepareAsync(string suffix)
    {
        var actor = await _db.AdminActorAsync();
        var chart = await _db.SeedAccountingAsync();
        var branch = await _db.CreateBranchAsync("SA-" + suffix, "فرع " + suffix);
        var warehouse = await _db.CreateWarehouseAsync("SAW-" + suffix, "مخزن " + suffix, branch);

        var category = await _db.ItemCategoryRepo.CreateAsync(new ItemCategoryInput
        {
            Code = "CAT-" + suffix, NameAr = "تصنيف " + suffix
        });
        var unit = await _db.UnitRepo.CreateAsync(new UnitInput
        {
            Code = "U-" + suffix, NameAr = "وحدة " + suffix
        });
        var item = await _db.ItemRepo.CreateAsync(new ItemInput
        {
            Code = "IT-" + suffix, NameAr = "صنف " + suffix, CategoryId = category.Id,
            BaseUnitId = unit.Id,
            Units = { new ItemUnitInput { UnitId = unit.Id, ConversionFactor = 1 } }
        });

        await _db.AccountingSettings().SaveCategoryAccountAsync(actor, new ItemCategoryAccountInput
        {
            CategoryId = category.Id,
            InventoryAccountId = chart["1120"].Id,
            CogsAccountId = chart["5100"].Id,
            AdjustmentGainAccountId = chart["5410"].Id,
            AdjustmentLossAccountId = chart["5420"].Id
        });

        return (item, branch, warehouse);
    }

    [Fact]
    public async Task Receipt_generates_balanced_inventory_journal_and_is_idempotent()
    {
        var actor = await _db.AdminActorAsync();
        var (item, branch, warehouse) = await PrepareAsync("RCP");
        var receipt = await _db.PostReceiptAsync(item, branch, warehouse, 10, 5);

        var preview = await _db.StockAccountingService().PreviewAsync(actor, receipt.Id);
        Assert.True(preview.CanPost, preview.ErrorKey);
        Assert.Equal(50m, preview.Lines.Sum(l => l.Debit));
        Assert.Equal(50m, preview.Lines.Sum(l => l.Credit));

        var entryId = await _db.StockAccountingService().GenerateAsync(actor, receipt.Id);
        var entry = await _db.JournalEntryRepo.GetAsync(entryId);

        Assert.Equal(EntryStatuses.Posted, entry!.Status);
        Assert.Equal(EntrySources.StockDocument, entry.SourceType);
        Assert.Equal(receipt.Id, entry.SourceId);
        Assert.Equal(50m, entry.TotalDebit);
        Assert.Equal(50m, entry.TotalCredit);

        var again = await _db.StockAccountingService().GenerateAsync(actor, receipt.Id);
        Assert.Equal(entryId, again);

        var reports = await _db.AccountingReports().StockVsGlAsync(actor);
        var row = Assert.Single(reports, r => r.StockValue != 0 || r.GlBalance != 0);
        Assert.Equal(50m, row.StockValue);
        Assert.Equal(50m, row.GlBalance);
        Assert.True(row.IsMatched);
    }

    [Fact]
    public async Task Auto_post_creates_the_journal_when_the_document_is_posted()
    {
        var actor = await _db.AdminActorAsync();
        var (item, branch, warehouse) = await PrepareAsync("AUTO");

        var settings = await _db.AccountingSettings().GetAsync(actor);
        settings.AutoPostStockJournals = true;
        settings.AccountingStartDate = new DateTime(2026, 1, 1);
        await _db.AccountingSettings().SaveAsync(actor, settings);

        var draft = await _db.StockDocumentService().SaveAsync(actor, null, 0, new StockDocumentInput
        {
            DocType = StockDocumentTypes.Receipt,
            DocDate = Date,
            BranchId = branch,
            WarehouseId = warehouse,
            Lines =
            {
                new StockDocumentLineInput
                {
                    ItemId = item.Id, UnitId = item.BaseUnitId, ConversionFactor = 1,
                    Quantity = 2, UnitCost = 25
                }
            }
        });
        await _db.StockDocumentService().PostAsync(actor, draft.Document.Id);

        var entry = await _db.JournalEntryRepo.GetBySourceAsync(
            EntrySources.StockDocument, draft.Document.Id);
        Assert.NotNull(entry);
        Assert.Equal(EntryStatuses.Posted, entry!.Status);
        Assert.Equal(50m, entry.TotalDebit);
    }
}
