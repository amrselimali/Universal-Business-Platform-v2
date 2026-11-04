using Dapper;
using Npgsql;
using Ubp.Core.Models;
using Ubp.Core.Results;
using Xunit;

namespace Ubp.Tests;

/// <summary>
/// مصروفات تشغيلية غير مرتبطة بدورة الحجز: سطور على شجرة الحسابات، سداد نقدي (سند صرف) أو آجل
/// على حساب المورد، وبنود مخزنية اختيارية تُستلم في المخزن وتُرحّل على حساب المخزون.
/// </summary>
public sealed class OperatingExpenseTests : IClassFixture<TestDb>
{
    private readonly TestDb _db;

    public OperatingExpenseTests(TestDb db) => _db = db;

    private sealed record Fixture(Guid Branch, Guid Warehouse, Item Item, Partner Supplier,
        Account ExpenseAccount, Account InventoryAccount, Guid TreasuryId, Guid CashMethod);

    private async Task<Fixture> SetupAsync(string suffix)
    {
        var actor = await _db.AdminActorAsync();
        var chart = await _db.SeedAccountingAsync();
        var branch = await _db.CreateBranchAsync("EXP-B-" + suffix, "فرع مصروفات " + suffix);
        var warehouse = await _db.CreateWarehouseAsync("EXP-W-" + suffix,
            "مخزن مصروفات " + suffix, branch);
        var item = await _db.CreateItemAsync("EXP-IT-" + suffix, "صنف مصروفات " + suffix);
        var supplier = await _db.PartnerRepo.CreateAsync(PartnerKind.Supplier, new PartnerInput
        { Code = "EXP-S-" + suffix, NameAr = "مورد مصروفات " + suffix });

        var treasuryId = Guid.CreateVersion7();
        await _db.TreasuryRepo.CreateTreasuryAsync(treasuryId, new TreasuryInput
        {
            BranchId = branch, Code = "EXP-T-" + suffix, NameAr = "خزينة مصروفات " + suffix,
            TreasuryType = TreasuryTypes.Cash, AccountId = chart["1101"].Id
        }, actor.UserId);
        var cash = Guid.CreateVersion7();
        await _db.TreasuryRepo.CreateMethodAsync(cash, new TreasuryMethodInput
        { TreasuryId = treasuryId, Code = "CASH", NameAr = "نقدي" }, actor.UserId);

        return new Fixture(branch, warehouse, item, supplier, chart["5300"], chart["2150"],
            treasuryId, cash);
    }

    private OperatingExpenseInput Input(Fixture f, OperatingExpenseLineInput line) => new()
    {
        BranchId = f.Branch, DocDate = DateTime.Today, SupplierId = f.Supplier.Id,
        Lines = { line }
    };

    private static OperatingExpenseLineInput ExpenseLine(Account account, decimal quantity,
        decimal price, string? description = "مصروف تشغيلي") => new()
    {
        AccountId = account.Id, Description = description, Quantity = quantity, UnitPrice = price
    };

    [Fact]
    public async Task Expense_on_credit_posts_the_chosen_account_and_the_supplier_payable()
    {
        var actor = await _db.AdminActorAsync();
        var f = await SetupAsync("1");

        var created = await _db.OperatingExpenses().CreateAsync(actor,
            Input(f, ExpenseLine(f.ExpenseAccount, 1, 500)));
        Assert.Equal(OperatingExpenseStatuses.Draft, created.Status);
        Assert.Equal(500m, created.TotalAmount);

        var posted = await _db.OperatingExpenses().PostAsync(actor, created.Id);

        Assert.Equal(OperatingExpenseStatuses.Posted, posted.Status);
        Assert.StartsWith("EXP-", posted.DocNo);
        Assert.Null(posted.VoucherId);

        await using var connection = new NpgsqlConnection(TestDb.AdminConnection);
        await connection.OpenAsync();
        var entries = (await connection.QueryAsync<(string Code, decimal Debit, decimal Credit)>(
            """
            SELECT a.code, l.debit, l.credit FROM journal_entry_lines l
            JOIN accounts a ON a.id = l.account_id
            WHERE l.entry_id = @entryId
            """, new { entryId = posted.JournalEntryId })).ToList();
        Assert.Contains(entries, l => l.Code == "5300" && l.Debit == 500m);
        Assert.Contains(entries, l => l.Code == "2100" && l.Credit == 500m);

        var supplierBalance = await connection.ExecuteScalarAsync<decimal>("""
            SELECT COALESCE(SUM(l.debit - l.credit), 0) FROM journal_entry_lines l
            JOIN accounts a ON a.id = l.account_id
            WHERE a.code = '2100' AND l.supplier_id = @supplierId
            """, new { supplierId = f.Supplier.Id });
        Assert.Equal(-500m, supplierBalance);
    }

    [Fact]
    public async Task Expense_paid_from_a_treasury_posts_a_payment_voucher_and_clears_the_supplier()
    {
        var actor = await _db.AdminActorAsync();
        var f = await SetupAsync("2");

        var input = Input(f, ExpenseLine(f.ExpenseAccount, 2, 250));
        input.TreasuryId = f.TreasuryId;
        input.TreasuryMethodId = f.CashMethod;

        var created = await _db.OperatingExpenses().CreateAsync(actor, input);
        var posted = await _db.OperatingExpenses().PostAsync(actor, created.Id);

        Assert.True(posted.IsPaid);
        Assert.NotNull(posted.VoucherId);

        await using var connection = new NpgsqlConnection(TestDb.AdminConnection);
        await connection.OpenAsync();

        var voucher = await connection.QuerySingleAsync<(string Status, decimal Total, string DocNo)>(
            "SELECT status AS Status, total_amount AS Total, doc_no AS DocNo " +
            "FROM vouchers WHERE id = @id", new { id = posted.VoucherId });
        Assert.Equal("posted", voucher.Status);
        Assert.Equal(500m, voucher.Total);
        Assert.StartsWith("PAY-", voucher.DocNo);

        var cash = await connection.ExecuteScalarAsync<decimal>("""
            SELECT COALESCE(SUM(l.debit - l.credit), 0) FROM journal_entry_lines l
            JOIN accounts a ON a.id = l.account_id
            WHERE a.code = '1101'
              AND l.entry_id = (SELECT journal_entry_id FROM vouchers WHERE id = @id)
            """, new { id = posted.VoucherId });
        Assert.Equal(-500m, cash);

        var supplierBalance = await connection.ExecuteScalarAsync<decimal>("""
            SELECT COALESCE(SUM(l.debit - l.credit), 0) FROM journal_entry_lines l
            JOIN accounts a ON a.id = l.account_id
            WHERE a.code = '2100' AND l.supplier_id = @supplierId
            """, new { supplierId = f.Supplier.Id });
        Assert.Equal(0m, supplierBalance);
    }

    [Fact]
    public async Task Stock_line_receives_the_item_and_posts_the_inventory_account()
    {
        var actor = await _db.AdminActorAsync();
        var f = await SetupAsync("3");

        var line = ExpenseLine(f.InventoryAccount, 3, 50, "شراء مستلزمات");
        line.ItemId = f.Item.Id;
        line.UnitId = f.Item.BaseUnitId;
        line.WarehouseId = f.Warehouse;

        var created = await _db.OperatingExpenses().CreateAsync(actor, Input(f, line));
        var posted = await _db.OperatingExpenses().PostAsync(actor, created.Id);

        Assert.NotNull(posted.StockDocumentId);
        Assert.Equal(150m, posted.TotalAmount);
        Assert.Equal(3m, await _db.OnHandAsync(f.Item.Id, f.Warehouse));

        await using var connection = new NpgsqlConnection(TestDb.AdminConnection);
        await connection.OpenAsync();
        Assert.Equal(1, await connection.ExecuteScalarAsync<int>(
            "SELECT count(*)::int FROM stock_movements WHERE item_id = @id AND direction = 'IN'",
            new { id = f.Item.Id }));

        var entries = (await connection.QueryAsync<(string Code, decimal Debit, decimal Credit)>(
            """
            SELECT a.code, l.debit, l.credit FROM journal_entry_lines l
            JOIN accounts a ON a.id = l.account_id
            WHERE l.entry_id = @entryId
            """, new { entryId = posted.JournalEntryId })).ToList();
        Assert.Contains(entries, l => l.Code == "2150" && l.Debit == 150m);
        Assert.Contains(entries, l => l.Code == "2100" && l.Credit == 150m);
    }

    [Fact]
    public async Task Expense_validation_rejects_non_postable_accounts_and_missing_supplier()
    {
        var actor = await _db.AdminActorAsync();
        var f = await SetupAsync("4");
        var chart = await _db.SeedAccountingAsync();

        var groupAccount = new OperatingExpenseLineInput
        { AccountId = chart["5000"].Id, Quantity = 1, UnitPrice = 10 };
        var error = await Assert.ThrowsAsync<ValidationAppException>(() =>
            _db.OperatingExpenses().CreateAsync(actor, Input(f, groupAccount)));
        Assert.Equal("validation.expense_account_not_postable", error.Code);

        var noSupplier = Input(f, ExpenseLine(f.ExpenseAccount, 1, 10));
        noSupplier.SupplierId = Guid.Empty;
        var supplierError = await Assert.ThrowsAsync<ValidationAppException>(() =>
            _db.OperatingExpenses().CreateAsync(actor, noSupplier));
        Assert.Equal("validation.required_fields", supplierError.Code);
    }
}
