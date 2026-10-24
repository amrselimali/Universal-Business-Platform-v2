using Dapper;
using Npgsql;
using Ubp.Core.Models;
using Ubp.Core.Results;
using Xunit;

namespace Ubp.Tests;

public sealed class SalesFinanceTests : IClassFixture<TestDb>
{
    private readonly TestDb _db;

    public SalesFinanceTests(TestDb db) => _db = db;

    private sealed record Fixture(Dictionary<string, Account> Chart, Guid Branch, Guid Warehouse,
        Item Item, Partner Customer, Employee Employee, Guid TreasuryId, Guid MethodId,
        Booking Booking, Guid BookingLineId, Tax Tax);

    private async Task<Fixture> SetupAsync(string suffix)
    {
        var actor = await _db.AdminActorAsync();
        var chart = await _db.SeedAccountingAsync();
        await _db.AccountingSettings().SetDefaultAsync(actor, PostingKeys.CustomerAdvances,
            chart["1110"].Id);
        await _db.AccountingSettings().SetDefaultAsync(actor, PostingKeys.BookingForfeitIncome,
            chart["4900"].Id);

        var branch = await _db.CreateBranchAsync("SF-B-" + suffix, "فرع مالي " + suffix);
        var warehouse = await _db.CreateWarehouseAsync("SF-W-" + suffix, "مخزن " + suffix, branch);
        var item = await _db.CreateItemAsync("SF-IT-" + suffix, "صنف " + suffix);
        await _db.PostReceiptAsync(item, branch, warehouse, 10, 50);
        var customer = await _db.PartnerRepo.CreateAsync(PartnerKind.Customer, new PartnerInput
        { Code = "SF-C-" + suffix, NameAr = "عميل " + suffix });

        var tax = await _db.MasterData().SaveTaxAsync(actor, null, 0, new TaxInput
        { Code = "SF-TAX-" + suffix, NameAr = "ضريبة " + suffix, Rate = 14m });

        var employeeId = Guid.CreateVersion7();
        await _db.EmployeeRepo.CreateAsync(employeeId, new EmployeeInput
        { Code = "SF-E-" + suffix, NameAr = "موظف " + suffix, IsSeller = true }, actor.UserId);
        var employee = (await _db.EmployeeRepo.GetAsync(employeeId))!;

        var booking = await _db.BookingService().CreateAsync(actor, new BookingInput
        {
            BranchId = branch, BookingDate = DateTime.Today, CustomerId = customer.Id,
            AssignedEmployeeId = employeeId,
            Lines =
            {
                new BookingLineInput
                {
                    ItemId = item.Id, UnitId = item.BaseUnitId, WarehouseId = warehouse,
                    Quantity = 2, UnitPrice = 100, DiscountPercent = 10, TaxId = tax.Id
                }
            }
        });
        await _db.BookingService().ConfirmAsync(actor, booking.Id);
        var confirmed = await _db.BookingService().GetAsync(actor, booking.Id);

        var treasuryId = Guid.CreateVersion7();
        await _db.TreasuryRepo.CreateTreasuryAsync(treasuryId, new TreasuryInput
        {
            BranchId = branch, Code = "SF-T-" + suffix, NameAr = "خزينة " + suffix,
            TreasuryType = TreasuryTypes.Cash, AccountId = chart["1101"].Id
        }, actor.UserId);
        var methodId = Guid.CreateVersion7();
        await _db.TreasuryRepo.CreateMethodAsync(methodId, new TreasuryMethodInput
        { TreasuryId = treasuryId, Code = "CASH", NameAr = "نقدي" }, actor.UserId);

        return new Fixture(chart, branch, warehouse, item, customer, employee, treasuryId,
            methodId, confirmed, confirmed.Lines.Single().Id, tax);
    }

    private async Task<Voucher> ReceiptAsync(Fixture f, decimal amount)
    {
        var actor = await _db.AdminActorAsync();
        var voucher = await _db.TreasuryService().CreateVoucherAsync(actor, new VoucherInput
        {
            VoucherType = VoucherTypes.Receipt, BranchId = f.Branch, DocDate = DateTime.Today,
            PartyType = "C", PartyId = f.Customer.Id, PartyName = f.Customer.NameAr,
            TreasuryId = f.TreasuryId,
            Lines = { new VoucherLineInput { TreasuryMethodId = f.MethodId, Amount = amount } }
        });
        return await _db.TreasuryService().PostVoucherAsync(actor, voucher.Id);
    }

    private async Task<CommercialDocument> InvoiceAsync(Fixture f, decimal receiptAmount,
        decimal allocate)
    {
        var actor = await _db.AdminActorAsync();
        var settings = await _db.Settings().GetAsync();
        settings.AutoInvoiceOnDelivery = false;
        await _db.Settings().UpdateAsync(actor, settings);

        var note = await _db.Fulfillment().CreateDeliveryAsync(actor, new DeliveryInput
        {
            BookingId = f.Booking.Id, BranchId = f.Branch, DocDate = DateTime.Today,
            WarehouseId = f.Warehouse,
            Lines = { new DeliveryLineInput { BookingLineId = f.BookingLineId, Quantity = 2 } }
        });
        await _db.Fulfillment().PostDeliveryAsync(actor, note.Id);

        var draft = await _db.SalesInvoices().CreateAsync(actor, new CommercialDocumentInput
        {
            DocType = CommercialDocTypes.SalesInvoice, BranchId = f.Branch,
            CustomerId = f.Customer.Id, BookingId = f.Booking.Id, DocDate = DateTime.Today,
            Lines =
            {
                new CommercialDocumentLineInput
                {
                    ItemId = f.Item.Id, UnitId = f.Item.BaseUnitId, Quantity = 2,
                    UnitPrice = 100, DiscountPercent = 10, TaxId = f.Tax.Id,
                    BookingLineId = f.BookingLineId
                }
            }
        });
        var invoice = await _db.SalesInvoices().PostAsync(actor, draft.Header.Id);
        if (receiptAmount > 0) await ReceiptAsync(f, receiptAmount);
        if (allocate > 0)
        {
            var vouchers = await _db.TreasuryRepo.ListVouchersAsync(f.Branch,
                VoucherTypes.Receipt, VoucherStatuses.Posted, 10);
            await _db.TreasuryService().AllocateAsync(actor, vouchers.First().Id, invoice.Id, allocate);
        }
        return invoice;
    }

    private async Task<Settlement> SettlementAsync(Fixture f, string type, decimal amount,
        bool approve)
    {
        var actor = await _db.AdminActorAsync();
        var settlement = await _db.SalesFinance().CreateSettlementAsync(actor, new SettlementInput
        {
            BranchId = f.Branch, BookingId = f.Booking.Id, SettlementType = type, Amount = amount,
            TreasuryId = SettlementTypes.HasRefund(type) ? f.TreasuryId : null,
            Reason = "اختبار"
        });
        if (approve) await _db.SalesFinance().ApproveSettlementAsync(actor, settlement.Id);
        return settlement;
    }

    [Fact]
    public async Task Forfeit_settlement_posts_income()
    {
        var actor = await _db.AdminActorAsync();
        var f = await SetupAsync("1");
        var settlement = await SettlementAsync(f, SettlementTypes.Forfeit, 100, true);
        var posted = await _db.SalesFinance().PostSettlementAsync(actor, settlement.Id);

        Assert.Equal(CommercialStatuses.Posted, posted.Status);
        Assert.StartsWith("SET-", posted.DocNo);

        await using var connection = new NpgsqlConnection(TestDb.AdminConnection);
        await connection.OpenAsync();
        var lines = (await connection.QueryAsync<(string Code, decimal Debit, decimal Credit)>("""
            SELECT a.code, l.debit, l.credit FROM journal_entry_lines l
            JOIN accounts a ON a.id = l.account_id WHERE l.entry_id = @id
            """, new { id = posted.JournalEntryId })).ToList();
        Assert.Contains(lines, l => l.Code == "1110" && l.Debit == 100m);
        Assert.Contains(lines, l => l.Code == "4900" && l.Credit == 100m);
    }

    [Fact]
    public async Task Refund_settlement_pays_from_treasury()
    {
        var actor = await _db.AdminActorAsync();
        var f = await SetupAsync("2");
        var settlement = await SettlementAsync(f, SettlementTypes.Refund, 50, true);
        var posted = await _db.SalesFinance().PostSettlementAsync(actor, settlement.Id);

        await using var connection = new NpgsqlConnection(TestDb.AdminConnection);
        await connection.OpenAsync();
        var lines = (await connection.QueryAsync<(string Code, decimal Debit, decimal Credit)>("""
            SELECT a.code, l.debit, l.credit FROM journal_entry_lines l
            JOIN accounts a ON a.id = l.account_id WHERE l.entry_id = @id
            """, new { id = posted.JournalEntryId })).ToList();
        Assert.Contains(lines, l => l.Code == "1110" && l.Debit == 50m);
        Assert.Contains(lines, l => l.Code == "1101" && l.Credit == 50m);
    }

    [Fact]
    public async Task Cancel_qty_releases_reservation_and_cancels_booking()
    {
        var actor = await _db.AdminActorAsync();
        var f = await SetupAsync("3");
        Assert.Equal(8m, await _db.BookingService().AvailableAsync(actor, f.Item.Id, f.Warehouse));

        var settlement = await SettlementAsync(f, SettlementTypes.CancelQty, 0, false);
        await _db.SalesFinance().PostSettlementAsync(actor, settlement.Id);

        Assert.Equal(10m, await _db.BookingService().AvailableAsync(actor, f.Item.Id, f.Warehouse));
        Assert.Equal(BookingStatuses.Cancelled,
            (await _db.BookingService().GetAsync(actor, f.Booking.Id)).Status);
    }

    [Fact]
    public async Task Settlement_requires_approval_before_posting()
    {
        var actor = await _db.AdminActorAsync();
        var f = await SetupAsync("4");
        var settlement = await SettlementAsync(f, SettlementTypes.Forfeit, 100, false);
        var error = await Assert.ThrowsAsync<ValidationAppException>(() =>
            _db.SalesFinance().PostSettlementAsync(actor, settlement.Id));
        Assert.Equal("validation.settlement_not_approved", error.Code);
    }

    [Fact]
    public async Task Commission_basis_three_modes()
    {
        var actor = await _db.AdminActorAsync();
        var f = await SetupAsync("5");
        await InvoiceAsync(f, 300, 100);
        var month = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);

        await using var connection = new NpgsqlConnection(TestDb.AdminConnection);
        await connection.OpenAsync();
        async Task<decimal> CalculateAsync(short basis)
        {
            await connection.ExecuteAsync("DELETE FROM employee_commissions WHERE period_month = @m",
                new { m = month });
            await _db.SalesFinance().CalculateCommissionsAsync(actor,
                new CommissionInput { Month = month, Rate = 10m, Basis = basis });
            var rows = await _db.SalesFinance().ListCommissionsAsync(actor, month, f.Employee.Id);
            return rows.Sum(r => r.Amount);
        }

        Assert.Equal(18m, await CalculateAsync(1));
        Assert.Equal(20.52m, await CalculateAsync(2));
        Assert.Equal(10m, await CalculateAsync(3));
    }

    [Fact]
    public async Task Aging_by_invoice_and_by_quantity_list_details()
    {
        var actor = await _db.AdminActorAsync();
        var f = await SetupAsync("7");
        var invoice = await InvoiceAsync(f, 0, 0);

        var invoices = await _db.SalesFinance().AgingInvoicesAsync(actor, f.Branch, DateTime.Today);
        var invoiceRow = invoices.Single(x => x.DocumentId == invoice.Id);
        Assert.Equal(invoice.Total, invoiceRow.Balance);
        Assert.Equal(invoice.DocNo, invoiceRow.DocNo);

        var booking = await _db.BookingService().CreateAsync(actor, new BookingInput
        {
            BranchId = f.Branch, CustomerId = f.Customer.Id, BookingDate = DateTime.Today.AddDays(-20),
            Lines = { new BookingLineInput { ItemId = f.Item.Id, UnitId = f.Item.BaseUnitId,
                WarehouseId = f.Warehouse, Quantity = 3, UnitPrice = 100 } }
        });
        await _db.BookingService().ConfirmAsync(actor, booking.Id);

        var quantities = await _db.SalesFinance().AgingQuantitiesAsync(actor, f.Branch,
            DateTime.Today);
        var qtyRow = quantities.Single(x => x.BookingId == booking.Id);
        Assert.Equal(3m, qtyRow.RemainingQty);
        Assert.Equal(20, qtyRow.AgeDays);
        Assert.Equal("days30", qtyRow.Bucket);
    }

    [Fact]
    public async Task Aging_unposted_and_reconciliation_read_the_new_views()
    {
        var actor = await _db.AdminActorAsync();
        var f = await SetupAsync("6");
        var invoice = await InvoiceAsync(f, 300, 100);

        var customers = await _db.SalesFinance().AgingAsync(actor, "customers", f.Branch,
            DateTime.Today);
        var row = customers.Single(x => x.PartyId == f.Customer.Id);
        Assert.Equal(invoice.Total - 100m, row.Total);

        var advances = await _db.SalesFinance().AgingAsync(actor, "advances", f.Branch,
            DateTime.Today);
        Assert.Equal(200m, advances.Single(x => x.PartyId == f.Customer.Id).Total);

        var bookings = await _db.SalesFinance().AgingAsync(actor, "bookings", f.Branch,
            DateTime.Today);
        Assert.DoesNotContain(bookings, x => x.PartyId == f.Booking.Id);

        await using var connection = new NpgsqlConnection(TestDb.AdminConnection);
        await connection.OpenAsync();
        await connection.ExecuteAsync("""
            INSERT INTO vouchers (id, branch_id, doc_date, status, voucher_type, party_type,
                party_id, party_name, treasury_id, total_amount)
            VALUES (@id, @branch, current_date, 'posted', 'receipt', 'C', @party, 'عميل',
                    @treasury, 10)
            """, new { id = Guid.CreateVersion7(), branch = f.Branch, party = f.Customer.Id,
                treasury = f.TreasuryId });

        var unposted = await _db.SalesFinance().ListUnpostedAsync(actor, f.Branch);
        Assert.NotEmpty(unposted);

        var checks = await _db.SalesFinance().ReconcileAsync(actor, f.Branch);
        Assert.Equal(3, checks.Count);
        Assert.Contains(checks, c => c.Key == "customers");
    }
}
