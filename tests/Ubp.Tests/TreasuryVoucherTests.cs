using Dapper;
using Npgsql;
using Ubp.Core.Models;
using Ubp.Core.Results;
using Xunit;

namespace Ubp.Tests;

public sealed class TreasuryVoucherTests : IClassFixture<TestDb>
{
    private readonly TestDb _db;

    public TreasuryVoucherTests(TestDb db) => _db = db;

    private sealed record Fixture(Guid Branch, Guid TreasuryId, Guid CashMethod, Guid CardMethod,
        Partner Customer, Dictionary<string, Account> Chart);

    /// <summary>فرع + خزينة نقدية + طريقتا دفع (نقدي، وبطاقة بعمولة 2% وحساب وسيط).</summary>
    private async Task<Fixture> SetupAsync(string suffix)
    {
        var actor = await _db.AdminActorAsync();
        var chart = await _db.SeedAccountingAsync();
        await MapDefaultsAsync(actor, chart);

        var branch = await _db.CreateBranchAsync("TV-B-" + suffix, "فرع خزينة " + suffix);
        var treasuryId = Guid.CreateVersion7();
        await _db.TreasuryRepo.CreateTreasuryAsync(treasuryId, new TreasuryInput
        {
            BranchId = branch, Code = "T-" + suffix, NameAr = "خزينة " + suffix,
            TreasuryType = TreasuryTypes.Cash, AccountId = chart["1101"].Id
        }, actor.UserId);

        var cash = Guid.CreateVersion7();
        await _db.TreasuryRepo.CreateMethodAsync(cash, new TreasuryMethodInput
        {
            TreasuryId = treasuryId, Code = "CASH", NameAr = "نقدي",
            MethodType = PaymentMethodTypes.Cash
        }, actor.UserId);

        var card = Guid.CreateVersion7();
        await _db.TreasuryRepo.CreateMethodAsync(card, new TreasuryMethodInput
        {
            TreasuryId = treasuryId, Code = "CARD", NameAr = "بطاقة",
            MethodType = PaymentMethodTypes.Card,
            ClearingAccountId = chart["1130"].Id, FeePercent = 2m
        }, actor.UserId);

        var customer = await _db.PartnerRepo.CreateAsync(PartnerKind.Customer, new PartnerInput
        {
            Code = "TV-C-" + suffix, NameAr = "عميل " + suffix
        });

        return new Fixture(branch, treasuryId, cash, card, customer, chart);
    }

    private async Task MapDefaultsAsync(Ubp.Core.Security.ActorContext actor,
        Dictionary<string, Account> chart)
    {
        var settings = _db.AccountingSettings();
        await settings.SetDefaultAsync(actor, PostingKeys.CustomerAdvances, chart["1110"].Id);
        await settings.SetDefaultAsync(actor, PostingKeys.SupplierAdvances, chart["2100"].Id);
        await settings.SetDefaultAsync(actor, PostingKeys.PaymentFees, chart["5200"].Id);
        await settings.SetDefaultAsync(actor, PostingKeys.CashOverShort, chart["5490"].Id);
        await settings.SetDefaultAsync(actor, PostingKeys.InterBranchClearing, chart["1130"].Id);
    }

    private static VoucherInput Receipt(Fixture f, params VoucherLineInput[] lines) => new()
    {
        VoucherType = VoucherTypes.Receipt, BranchId = f.Branch, DocDate = DateTime.Today,
        PartyType = "C", PartyId = f.Customer.Id, PartyName = f.Customer.NameAr,
        TreasuryId = f.TreasuryId, Lines = lines.ToList()
    };

    [Fact]
    public async Task Receipt_with_cash_and_card_posts_fee_and_customer_advance()
    {
        var actor = await _db.AdminActorAsync();
        var service = _db.TreasuryService();
        var f = await SetupAsync("1");

        var draft = await service.CreateVoucherAsync(actor, Receipt(f,
            new VoucherLineInput { TreasuryMethodId = f.CashMethod, Amount = 1000 },
            new VoucherLineInput { TreasuryMethodId = f.CardMethod, Amount = 500 }));

        Assert.True(draft.IsDraft);
        Assert.Equal(1500m, draft.TotalAmount);
        Assert.Equal(10m, draft.FeeTotal);

        var posted = await service.PostVoucherAsync(actor, draft.Id);
        Assert.Equal(VoucherStatuses.Posted, posted.Status);
        Assert.StartsWith("RCV-", posted.DocNo);
        Assert.NotNull(posted.JournalEntryId);

        await using var connection = new NpgsqlConnection(TestDb.AdminConnection);
        await connection.OpenAsync();
        var lines = (await connection.QueryAsync<(string Code, decimal Debit, decimal Credit)>("""
            SELECT a.code, l.debit, l.credit FROM journal_entry_lines l
            JOIN accounts a ON a.id = l.account_id
            WHERE l.entry_id = @id ORDER BY a.code
            """, new { id = posted.JournalEntryId })).ToList();

        Assert.Equal(1000m, lines.Single(x => x.Code == "1101").Debit);
        Assert.Equal(490m, lines.Single(x => x.Code == "1130").Debit);
        Assert.Equal(10m, lines.Single(x => x.Code == "5200").Debit);
        Assert.Equal(1500m, lines.Single(x => x.Code == "1110").Credit);

        var entry = await connection.QuerySingleAsync<(decimal Debit, decimal Credit, string Status)>(
            "SELECT total_debit, total_credit, status FROM journal_entries WHERE id = @id",
            new { id = posted.JournalEntryId });
        Assert.Equal(entry.Debit, entry.Credit);
        Assert.Equal("posted", entry.Status);

        var customerBalance = await connection.ExecuteScalarAsync<decimal>(
            "SELECT coalesce(sum(debit - credit), 0) FROM party_balances " +
            "WHERE party_type = 'C' AND party_id = @id", new { id = f.Customer.Id });
        Assert.Equal(-1500m, customerBalance);
    }
    /// <summary>فاتورة شراء مرحّلة (صنف واحد، بلا ضريبة) لتخصيص الدفعات عليها.</summary>
    private async Task<CommercialDocument> PostPurchaseInvoiceAsync(Fixture f, string suffix,
        decimal amount)
    {
        var actor = await _db.AdminActorAsync();
        var category = await _db.ItemCategoryRepo.CreateAsync(new ItemCategoryInput
        { Code = "TV-CAT-" + suffix, NameAr = "تصنيف " + suffix });
        var unit = await _db.UnitRepo.CreateAsync(new UnitInput
        { Code = "TV-U-" + suffix, NameAr = "وحدة " + suffix });
        var item = await _db.ItemRepo.CreateAsync(new ItemInput
        {
            Code = "TV-IT-" + suffix, NameAr = "صنف " + suffix, CategoryId = category.Id,
            BaseUnitId = unit.Id,
            Units = { new ItemUnitInput { UnitId = unit.Id, ConversionFactor = 1 } }
        });
        await _db.AccountingSettings().SaveCategoryAccountAsync(actor, new ItemCategoryAccountInput
        {
            CategoryId = category.Id, InventoryAccountId = f.Chart["1120"].Id,
            CogsAccountId = f.Chart["5100"].Id
        });

        var supplier = await _db.PartnerRepo.CreateAsync(PartnerKind.Supplier, new PartnerInput
        { Code = "TV-S-" + suffix, NameAr = "مورد " + suffix });
        var warehouse = await _db.CreateWarehouseAsync("TV-W-" + suffix, "مخزن " + suffix, f.Branch);

        var id = Guid.CreateVersion7();
        await _db.CommercialRepo.CreateDraftAsync(id, new CommercialDocumentInput
        {
            DocType = CommercialDocTypes.PurchaseInvoice, BranchId = f.Branch,
            WarehouseId = warehouse, DocDate = DateTime.Today, SupplierId = supplier.Id,
            Lines =
            {
                new CommercialDocumentLineInput
                {
                    ItemId = item.Id, UnitId = item.BaseUnitId, ConversionFactor = 1,
                    Quantity = 1, UnitPrice = amount
                }
            }
        }, actor.UserId);
        return await _db.CommercialRepo.PostAsync(id, actor.UserId, false);
    }

    private async Task<Voucher> PostPaymentAsync(Fixture f, Guid supplierId, string supplierName,
        decimal amount)
    {
        var actor = await _db.AdminActorAsync();
        var service = _db.TreasuryService();
        var draft = await service.CreateVoucherAsync(actor, new VoucherInput
        {
            VoucherType = VoucherTypes.Payment, BranchId = f.Branch, DocDate = DateTime.Today,
            PartyType = "S", PartyId = supplierId, PartyName = supplierName,
            TreasuryId = f.TreasuryId,
            Lines = { new VoucherLineInput { TreasuryMethodId = f.CashMethod, Amount = amount } }
        });
        return await service.PostVoucherAsync(actor, draft.Id);
    }

    [Fact]
    public async Task Payment_allocation_settles_invoice_and_rejects_duplicate_or_excess()
    {
        var actor = await _db.AdminActorAsync();
        var service = _db.TreasuryService();
        var f = await SetupAsync("2");
        var invoice = await PostPurchaseInvoiceAsync(f, "2", 300);
        var payment = await PostPaymentAsync(f, invoice.SupplierId!.Value,
            invoice.PartyName ?? "مورد", 250);

        var allocationId = await service.AllocateAsync(actor, payment.Id, invoice.Id, 250);
        Assert.NotEqual(Guid.Empty, allocationId);

        var allocations = await service.ListAllocationsAsync(actor, payment.Id);
        Assert.Single(allocations);
        Assert.Equal(250m, allocations[0].Amount);
        Assert.Equal(invoice.DocNo, allocations[0].TargetDocNo);

        var reloaded = await _db.CommercialRepo.GetAsync(invoice.Id);
        Assert.Equal(250m, reloaded!.AllocatedTotal);
        Assert.Equal(50m, reloaded.Balance);

        var duplicate = await Assert.ThrowsAsync<ValidationAppException>(() =>
            service.AllocateAsync(actor, payment.Id, invoice.Id, 10));
        Assert.Equal("validation.allocation_duplicate", duplicate.Code);

        var smallPayment = await PostPaymentAsync(f, invoice.SupplierId!.Value,
            invoice.PartyName ?? "مورد", 100);
        var secondInvoice = await PostPurchaseInvoiceAsync(f, "2b", 300);
        var excess = await Assert.ThrowsAsync<ValidationAppException>(() =>
            service.AllocateAsync(actor, smallPayment.Id, secondInvoice.Id, 150));
        Assert.Equal("validation.allocation_exceeds_voucher", excess.Code);
    }

    [Fact]
    public async Task Transfer_between_branches_clears_through_inter_branch_account()
    {
        var actor = await _db.AdminActorAsync();
        var service = _db.TreasuryService();
        var f = await SetupAsync("3");

        var targetBranch = await _db.CreateBranchAsync("TV-B2-3", "الفرع المستلم");
        var targetTreasury = Guid.CreateVersion7();
        await _db.TreasuryRepo.CreateTreasuryAsync(targetTreasury, new TreasuryInput
        {
            BranchId = targetBranch, Code = "T3-BNK", NameAr = "بنك الفرع المستلم",
            TreasuryType = TreasuryTypes.Bank, AccountId = f.Chart["1102"].Id
        }, actor.UserId);

        var draft = await service.CreateVoucherAsync(actor, new VoucherInput
        {
            VoucherType = VoucherTypes.Transfer, BranchId = f.Branch, DocDate = DateTime.Today,
            TreasuryId = f.TreasuryId, ToTreasuryId = targetTreasury,
            Lines = { new VoucherLineInput { TreasuryMethodId = f.CashMethod, Amount = 300 } }
        });
        var posted = await service.PostVoucherAsync(actor, draft.Id);

        Assert.StartsWith("TRF-", posted.DocNo);
        Assert.Equal(300m, posted.TotalAmount);

        await using var connection = new NpgsqlConnection(TestDb.AdminConnection);
        await connection.OpenAsync();

        var entries = (await connection.QueryAsync<(Guid Id, Guid BranchId, decimal Debit,
            decimal Credit)>("""
            SELECT id, branch_id, total_debit, total_credit FROM journal_entries
            WHERE reference = @docNo ORDER BY branch_id
            """, new { docNo = posted.DocNo })).ToList();

        Assert.Equal(2, entries.Count);
        Assert.All(entries, e => Assert.Equal(e.Debit, e.Credit));

        var source = entries.Single(e => e.BranchId == f.Branch);
        var sourceLines = (await connection.QueryAsync<(string Code, decimal Debit, decimal Credit)>("""
            SELECT a.code, l.debit, l.credit FROM journal_entry_lines l
            JOIN accounts a ON a.id = l.account_id WHERE l.entry_id = @id
            """, new { id = source.Id })).ToList();
        Assert.Equal(300m, sourceLines.Single(x => x.Code == "1130").Debit);
        Assert.Equal(300m, sourceLines.Single(x => x.Code == "1101").Credit);

        var destination = entries.Single(e => e.BranchId == targetBranch);
        var destinationLines = (await connection.QueryAsync<(string Code, decimal Debit, decimal Credit)>("""
            SELECT a.code, l.debit, l.credit FROM journal_entry_lines l
            JOIN accounts a ON a.id = l.account_id WHERE l.entry_id = @id
            """, new { id = destination.Id })).ToList();
        Assert.Equal(300m, destinationLines.Single(x => x.Code == "1102").Debit);
        Assert.Equal(300m, destinationLines.Single(x => x.Code == "1130").Credit);

        var clearingBalance = await connection.ExecuteScalarAsync<decimal>(
            "SELECT coalesce(sum(debit - credit), 0) FROM journal_entry_lines " +
            "WHERE account_id = @account AND entry_id = ANY(@entries)",
            new { account = f.Chart["1130"].Id, entries = entries.Select(e => e.Id).ToArray() });
        Assert.Equal(0m, clearingBalance);
    }

    [Fact]
    public async Task Posted_voucher_is_immutable_and_draft_can_be_deleted()
    {
        var actor = await _db.AdminActorAsync();
        var service = _db.TreasuryService();
        var f = await SetupAsync("4");

        var draft = await service.CreateVoucherAsync(actor, Receipt(f,
            new VoucherLineInput { TreasuryMethodId = f.CashMethod, Amount = 100 }));
        var deletable = await service.CreateVoucherAsync(actor, Receipt(f,
            new VoucherLineInput { TreasuryMethodId = f.CashMethod, Amount = 50 }));
        await service.DeleteVoucherAsync(actor, deletable.Id);
        Assert.Null(await _db.TreasuryRepo.GetVoucherAsync(deletable.Id));

        var posted = await service.PostVoucherAsync(actor, draft.Id);
        await using var connection = new NpgsqlConnection(TestDb.AdminConnection);
        await connection.OpenAsync();
        var update = await Assert.ThrowsAsync<Npgsql.PostgresException>(async () =>
            await connection.ExecuteAsync(
                "UPDATE vouchers SET notes = 'x' WHERE id = @id", new { id = posted.Id }));
        Assert.Equal("23514", update.SqlState);
    }

}
