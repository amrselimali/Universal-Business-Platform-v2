using Dapper;
using Npgsql;
using Ubp.Core.Models;
using Ubp.Core.Results;
using Xunit;

namespace Ubp.Tests;

public sealed class SalesInvoiceTests : IClassFixture<TestDb>
{
    private readonly TestDb _db;

    public SalesInvoiceTests(TestDb db) => _db = db;

    private sealed record Fixture(Dictionary<string, Account> Chart, Guid Branch, Guid Warehouse,
        Item Item, Partner Customer, Booking Booking, DeliveryNote Delivery, Guid BookingLineId);

    private async Task SetAutoInvoiceAsync(bool enabled)
    {
        var actor = await _db.AdminActorAsync();
        var settings = await _db.Settings().GetAsync();
        settings.AutoInvoiceOnDelivery = enabled;
        settings.InvoiceGroupingMode = 0;
        await _db.Settings().UpdateAsync(actor, settings);
    }

    private async Task<Fixture> SetupAsync(string suffix, bool autoInvoice)
    {
        var actor = await _db.AdminActorAsync();
        await SetAutoInvoiceAsync(autoInvoice);
        var chart = await _db.SeedAccountingAsync();
        await _db.AccountingSettings().SetDefaultAsync(actor, PostingKeys.CustomerAdvances,
            chart["1110"].Id);

        var branch = await _db.CreateBranchAsync("SI-B-" + suffix, "فرع بيع " + suffix);
        var warehouse = await _db.CreateWarehouseAsync("SI-W-" + suffix, "مخزن " + suffix, branch);
        var item = await _db.CreateItemAsync("SI-IT-" + suffix, "صنف " + suffix);
        await _db.PostReceiptAsync(item, branch, warehouse, 10, 50);
        var customer = await _db.PartnerRepo.CreateAsync(PartnerKind.Customer, new PartnerInput
        { Code = "SI-C-" + suffix, NameAr = "عميل " + suffix });

        var booking = await _db.BookingService().CreateAsync(actor, new BookingInput
        {
            BranchId = branch, BookingDate = DateTime.Today, CustomerId = customer.Id,
            Lines =
            {
                new BookingLineInput
                {
                    ItemId = item.Id, UnitId = item.BaseUnitId, WarehouseId = warehouse,
                    Quantity = 2, UnitPrice = 100
                }
            }
        });
        await _db.BookingService().ConfirmAsync(actor, booking.Id);
        var confirmed = await _db.BookingService().GetAsync(actor, booking.Id);
        var bookingLineId = confirmed.Lines.Single().Id;

        var note = await _db.Fulfillment().CreateDeliveryAsync(actor, new DeliveryInput
        {
            BookingId = booking.Id, BranchId = branch, DocDate = DateTime.Today,
            WarehouseId = warehouse,
            Lines = { new DeliveryLineInput { BookingLineId = bookingLineId, Quantity = 2 } }
        });
        var delivery = await _db.Fulfillment().PostDeliveryAsync(actor, note.Id);

        return new Fixture(chart, branch, warehouse, item, customer, confirmed, delivery,
            bookingLineId);
    }

    private async Task<CommercialDocumentDetail> CreateInvoiceAsync(Fixture f, decimal quantity)
    {
        var actor = await _db.AdminActorAsync();
        return await _db.SalesInvoices().CreateAsync(actor, new CommercialDocumentInput
        {
            DocType = CommercialDocTypes.SalesInvoice, BranchId = f.Branch,
            CustomerId = f.Customer.Id, BookingId = f.Booking.Id, DocDate = DateTime.Today,
            Lines =
            {
                new CommercialDocumentLineInput
                {
                    ItemId = f.Item.Id, UnitId = f.Item.BaseUnitId, Quantity = quantity,
                    UnitPrice = 100, BookingLineId = f.BookingLineId
                }
            }
        });
    }

    [Fact]
    public async Task Sales_line_requires_a_booking_line()
    {
        var f = await SetupAsync("1", false);
        var docId = Guid.CreateVersion7();

        await using var connection = new NpgsqlConnection(TestDb.AdminConnection);
        await connection.OpenAsync();
        await connection.ExecuteAsync("""
            INSERT INTO commercial_documents (id, branch_id, doc_date, doc_type, customer_id,
                status, total)
            VALUES (@docId, @branch, current_date, 'sales_invoice', @customer, 'draft', 0)
            """, new { docId, branch = f.Branch, customer = f.Customer.Id });

        var error = await Assert.ThrowsAsync<Npgsql.PostgresException>(async () =>
            await connection.ExecuteAsync("""
                INSERT INTO commercial_document_lines (document_id, line_no, item_id, unit_id,
                    quantity, unit_price)
                VALUES (@docId, 1, @item, @unit, 1, 100)
                """, new { docId, item = f.Item.Id, unit = f.Item.BaseUnitId }));

        Assert.Equal("23514", error.SqlState);
        Assert.Contains("SALES_LINE_REQUIRES_BOOKING", error.MessageText);
    }

    [Fact]
    public async Task Invoice_posts_revenue_and_consumes_customer_advance()
    {
        var actor = await _db.AdminActorAsync();
        var f = await SetupAsync("2", false);

        var treasuryId = Guid.CreateVersion7();
        await _db.TreasuryRepo.CreateTreasuryAsync(treasuryId, new TreasuryInput
        {
            BranchId = f.Branch, Code = "SI-T-2", NameAr = "خزينة البيع",
            TreasuryType = TreasuryTypes.Cash, AccountId = f.Chart["1101"].Id
        }, actor.UserId);
        var methodId = Guid.CreateVersion7();
        await _db.TreasuryRepo.CreateMethodAsync(methodId, new TreasuryMethodInput
        { TreasuryId = treasuryId, Code = "CASH", NameAr = "نقدي" }, actor.UserId);

        var receipt = await _db.TreasuryService().CreateVoucherAsync(actor, new VoucherInput
        {
            VoucherType = VoucherTypes.Receipt, BranchId = f.Branch, DocDate = DateTime.Today,
            PartyType = "C", PartyId = f.Customer.Id, PartyName = f.Customer.NameAr,
            TreasuryId = treasuryId,
            Lines = { new VoucherLineInput { TreasuryMethodId = methodId, Amount = 150 } }
        });
        await _db.TreasuryService().PostVoucherAsync(actor, receipt.Id);

        var draft = await CreateInvoiceAsync(f, 2);
        Assert.Equal(CommercialStatuses.Draft, draft.Header.Status);

        var posted = await _db.SalesInvoices().PostAsync(actor, draft.Header.Id);
        Assert.Equal(CommercialStatuses.Posted, posted.Status);
        Assert.StartsWith("SI-", posted.DocNo);
        Assert.NotNull(posted.JournalEntryId);

        await using var connection = new NpgsqlConnection(TestDb.AdminConnection);
        await connection.OpenAsync();
        var lines = (await connection.QueryAsync<(string Code, decimal Debit, decimal Credit)>("""
            SELECT a.code, l.debit, l.credit FROM journal_entry_lines l
            JOIN accounts a ON a.id = l.account_id
            WHERE l.entry_id = @id OR l.entry_id IN (
                SELECT id FROM journal_entries WHERE reference = @docNo AND source_type = 'invoice')
            """, new { id = posted.JournalEntryId, docNo = posted.DocNo })).ToList();

        Assert.Contains(lines, l => l.Code == "4100" && l.Credit == 200m);
        Assert.Contains(lines, l => l.Code == "1110" && l.Debit == 200m);
        Assert.Contains(lines, l => l.Code == "1110" && l.Debit == 150m);
        Assert.Contains(lines, l => l.Code == "1110" && l.Credit == 150m);

        var loaded = await _db.CommercialRepo.GetAsync(draft.Header.Id);
        Assert.Equal(150m, loaded!.AllocatedTotal);
        Assert.Equal(50m, loaded.Balance);

        var bookingLine = (await _db.BookingService().GetAsync(actor, f.Booking.Id)).Lines.Single();
        Assert.Equal(2m, bookingLine.InvoicedQty);

        var partyNet = await connection.ExecuteScalarAsync<decimal>(
            "SELECT coalesce(sum(debit - credit), 0) FROM party_balances " +
            "WHERE party_type = 'C' AND party_id = @id", new { id = f.Customer.Id });
        Assert.Equal(50m, partyNet);
    }

    [Fact]
    public async Task Return_posts_credit_note_and_restocks_at_original_cost()
    {
        var actor = await _db.AdminActorAsync();
        var f = await SetupAsync("3", false);
        var invoice = await CreateInvoiceAsync(f, 2);
        await _db.SalesInvoices().PostAsync(actor, invoice.Header.Id);
        var originalLine = (await _db.CommercialRepo.GetDetailAsync(invoice.Header.Id))!.Lines.Single();

        var returnDraft = await _db.SalesInvoices().CreateAsync(actor, new CommercialDocumentInput
        {
            DocType = CommercialDocTypes.SalesReturn, BranchId = f.Branch,
            CustomerId = f.Customer.Id, OriginalDocumentId = invoice.Header.Id,
            DocDate = DateTime.Today,
            Lines =
            {
                new CommercialDocumentLineInput
                {
                    ItemId = f.Item.Id, UnitId = f.Item.BaseUnitId, Quantity = 1,
                    UnitPrice = 100, BookingLineId = f.BookingLineId,
                    OriginalLineId = originalLine.Id
                }
            }
        });
        var posted = await _db.SalesInvoices().PostAsync(actor, returnDraft.Header.Id);

        Assert.Equal(CommercialStatuses.Posted, posted.Status);
        Assert.StartsWith("SR-", posted.DocNo);
        Assert.Equal(9m, await _db.OnHandAsync(f.Item.Id, f.Warehouse));

        await using var connection = new NpgsqlConnection(TestDb.AdminConnection);
        await connection.OpenAsync();
        var entries = (await connection.QueryAsync<(string Code, decimal Debit, decimal Credit)>("""
            SELECT a.code, l.debit, l.credit FROM journal_entry_lines l
            JOIN accounts a ON a.id = l.account_id
            JOIN journal_entries e ON e.id = l.entry_id
            WHERE e.reference = @docNo AND e.source_type = 'invoice'
            """, new { docNo = posted.DocNo })).ToList();

        Assert.Contains(entries, l => l.Code == "4100" && l.Debit == 100m);
        Assert.Contains(entries, l => l.Code == "1110" && l.Credit == 100m);
        Assert.Contains(entries, l => l.Code == "2150" && l.Debit == 50m);
        Assert.Contains(entries, l => l.Code == "5100" && l.Credit == 50m);

        var returned = await connection.ExecuteScalarAsync<decimal>(
            "SELECT returned_qty FROM commercial_document_lines WHERE id = @id",
            new { id = originalLine.Id });
        Assert.Equal(1m, returned);

        var error = await Assert.ThrowsAsync<Npgsql.PostgresException>(async () =>
            await connection.ExecuteAsync("""
                INSERT INTO sales_return_links (return_line_id, original_line_id, quantity)
                VALUES (@returnLineId, @originalLineId, 2)
                """, new { returnLineId = originalLine.Id, originalLineId = originalLine.Id }));
        Assert.Equal("23514", error.SqlState);
        Assert.Contains("RETURN_QTY_EXCEEDED", error.MessageText);
    }

    [Fact]
    public async Task Auto_invoice_on_delivery_creates_linked_invoice()
    {
        var f = await SetupAsync("4", true);

        await using var connection = new NpgsqlConnection(TestDb.AdminConnection);
        await connection.OpenAsync();
        var invoiceId = await connection.ExecuteScalarAsync<Guid?>(
            "SELECT invoice_id FROM delivery_notes WHERE id = @id", new { id = f.Delivery.Id });
        Assert.NotNull(invoiceId);

        var invoice = await _db.CommercialRepo.GetAsync(invoiceId!.Value);
        Assert.Equal(CommercialStatuses.Posted, invoice!.Status);
        Assert.Equal(200m, invoice.Total);
        Assert.StartsWith("SI-", invoice.DocNo);
    }

    [Fact]
    public async Task Invoice_quantity_guard_blocks_over_invoicing()
    {
        var f = await SetupAsync("5", false);
        var error = await Assert.ThrowsAsync<ValidationAppException>(() => CreateInvoiceAsync(f, 3));
        Assert.Equal("validation.invoice_qty_exceeded", error.Code);
    }
}
