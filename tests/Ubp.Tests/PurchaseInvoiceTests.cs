using Dapper;
using Npgsql;
using Ubp.Core.Models;
using Xunit;

namespace Ubp.Tests;

public sealed class PurchaseInvoiceTests : IClassFixture<TestDb>
{
    private readonly TestDb _db;

    public PurchaseInvoiceTests(TestDb db) => _db = db;

    private async Task<(Guid Branch, Guid Warehouse, Item Item, Partner Supplier, Tax Tax)>
        SetupAsync(string suffix)
    {
        var actor = await _db.AdminActorAsync();
        var chart = await _db.SeedAccountingAsync();
        var branch = await _db.CreateBranchAsync("PI-B-" + suffix, "فرع " + suffix);
        var warehouse = await _db.CreateWarehouseAsync("PI-W-" + suffix, "مخزن " + suffix, branch);

        var category = await _db.ItemCategoryRepo.CreateAsync(new ItemCategoryInput
        { Code = "PI-CAT-" + suffix, NameAr = "تصنيف " + suffix });
        var unit = await _db.UnitRepo.CreateAsync(new UnitInput
        { Code = "PI-U-" + suffix, NameAr = "وحدة " + suffix });
        var tax = await _db.MasterData().SaveTaxAsync(actor, null, 0,
            new TaxInput { Code = "PI-VAT-" + suffix, NameAr = "ضريبة 14%", Rate = 14m });

        var item = await _db.ItemRepo.CreateAsync(new ItemInput
        {
            Code = "PI-IT-" + suffix, NameAr = "صنف " + suffix, CategoryId = category.Id,
            BaseUnitId = unit.Id, TaxId = tax.Id, TrackExpiry = true,
            Units = { new ItemUnitInput { UnitId = unit.Id, ConversionFactor = 1 } }
        });

        await _db.AccountingSettings().SaveCategoryAccountAsync(actor, new ItemCategoryAccountInput
        {
            CategoryId = category.Id, InventoryAccountId = chart["1120"].Id,
            CogsAccountId = chart["5100"].Id
        });

        var supplier = await _db.PartnerRepo.CreateAsync(PartnerKind.Supplier,
            new PartnerInput { Code = "PI-SUP-" + suffix, NameAr = "مورد " + suffix });

        return (branch, warehouse, item, supplier, tax);
    }

    private async Task<CommercialDocument> DraftAsync(Guid branch, Guid warehouse, Partner supplier,
        string docType, Item item, Tax tax, decimal quantity, decimal price, Guid? original = null)
    {
        var actor = await _db.AdminActorAsync();
        var id = Guid.CreateVersion7();
        await _db.CommercialRepo.CreateDraftAsync(id, new CommercialDocumentInput
        {
            DocType = docType, BranchId = branch, WarehouseId = warehouse, DocDate = DateTime.Today,
            SupplierId = supplier.Id, OriginalDocumentId = original,
            Lines =
            {
                new CommercialDocumentLineInput
                {
                    ItemId = item.Id, UnitId = item.BaseUnitId, ConversionFactor = 1,
                    Quantity = quantity, UnitPrice = price, TaxId = tax.Id,
                    LotNumber = "L-" + quantity, ExpiryDate = DateTime.Today.AddMonths(6)
                }
            }
        }, actor.UserId);
        return (await _db.CommercialRepo.GetAsync(id))!;
    }

    private async Task<CommercialDocument> PostPurchaseInvoiceAsync(string suffix, decimal quantity,
        decimal price)
    {
        var actor = await _db.AdminActorAsync();
        var (branch, warehouse, item, supplier, tax) = await SetupAsync(suffix);
        var draft = await DraftAsync(branch, warehouse, supplier,
            CommercialDocTypes.PurchaseInvoice, item, tax, quantity, price);
        return await _db.CommercialRepo.PostAsync(draft.Id, actor.UserId, false);
    }

    [Fact]
    public async Task Purchase_invoice_posts_stock_lot_and_balanced_entry()
    {
        var actor = await _db.AdminActorAsync();
        var (branch, warehouse, item, supplier, tax) = await SetupAsync("1");

        var draft = await DraftAsync(branch, warehouse, supplier,
            CommercialDocTypes.PurchaseInvoice, item, tax, 10, 100);

        Assert.Equal(1000m, draft.Subtotal);
        Assert.Equal(140m, draft.TaxTotal);
        Assert.Equal(1140m, draft.Total);

        var posted = await _db.CommercialRepo.PostAsync(draft.Id, actor.UserId, false);

        Assert.Equal(CommercialStatuses.Posted, posted.Status);
        Assert.NotNull(posted.DocNo);
        Assert.NotNull(posted.JournalEntryId);
        Assert.NotNull(posted.StockDocumentId);
        Assert.Equal(10m, await _db.OnHandAsync(item.Id, warehouse));

        await using var connection = new NpgsqlConnection(TestDb.AdminConnection);
        await connection.OpenAsync();
        var entry = await connection.QuerySingleAsync<(decimal Debit, decimal Credit, string Status)>(
            "SELECT total_debit, total_credit, status FROM journal_entries WHERE id = @id",
            new { id = posted.JournalEntryId });
        Assert.Equal(1140m, entry.Debit);
        Assert.Equal(entry.Debit, entry.Credit);
        Assert.Equal("posted", entry.Status);

        var supplierBalance = await connection.ExecuteScalarAsync<decimal>(
            "SELECT coalesce(sum(debit - credit), 0) FROM party_balances " +
            "WHERE party_type = 'S' AND party_id = @id", new { id = supplier.Id });
        Assert.Equal(-1140m, supplierBalance);
    }

    [Fact]
    public async Task Purchase_return_reverses_stock_and_payable()
    {
        var actor = await _db.AdminActorAsync();
        var (branch, warehouse, item, supplier, tax) = await SetupAsync("2");

        var invoice = await DraftAsync(branch, warehouse, supplier,
            CommercialDocTypes.PurchaseInvoice, item, tax, 10, 100);
        var postedInvoice = await _db.CommercialRepo.PostAsync(invoice.Id, actor.UserId, false);

        var returnDraft = await DraftAsync(branch, warehouse, supplier,
            CommercialDocTypes.PurchaseReturn, item, tax, 4, 100, postedInvoice.Id);
        var postedReturn = await _db.CommercialRepo.PostAsync(returnDraft.Id, actor.UserId, false);

        Assert.Equal(CommercialStatuses.Posted, postedReturn.Status);
        Assert.Equal(6m, await _db.OnHandAsync(item.Id, warehouse));

        await using var connection = new NpgsqlConnection(TestDb.AdminConnection);
        await connection.OpenAsync();
        var entry = await connection.QuerySingleAsync<(decimal Debit, decimal Credit)>(
            "SELECT total_debit, total_credit FROM journal_entries WHERE id = @id",
            new { id = postedReturn.JournalEntryId });
        Assert.Equal(entry.Debit, entry.Credit);
        Assert.Equal(456m, entry.Debit);

        var supplierBalance = await connection.ExecuteScalarAsync<decimal>(
            "SELECT coalesce(sum(debit - credit), 0) FROM party_balances " +
            "WHERE party_type = 'S' AND party_id = @id", new { id = supplier.Id });
        Assert.Equal(-684m, supplierBalance);
    }

    [Fact]
    public async Task Posted_purchase_invoice_is_immutable()
    {
        var actor = await _db.AdminActorAsync();
        var (branch, warehouse, item, supplier, tax) = await SetupAsync("3");
        var draft = await DraftAsync(branch, warehouse, supplier,
            CommercialDocTypes.PurchaseInvoice, item, tax, 1, 50);
        var posted = await _db.CommercialRepo.PostAsync(draft.Id, actor.UserId, false);

        await using var connection = new NpgsqlConnection(TestDb.AdminConnection);
        await connection.OpenAsync();
        var update = await Assert.ThrowsAsync<Npgsql.PostgresException>(async () =>
            await connection.ExecuteAsync(
                "UPDATE commercial_documents SET notes = 'x' WHERE id = @id", new { id = posted.Id }));
        Assert.Equal("23514", update.SqlState);
    }
}
