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

    [Fact]
    public async Task Receipt_with_cash_and_card_posts_fee_and_customer_advance()
    {
        var actor = await _db.AdminActorAsync();
        var service = _db.TreasuryService();
        var f = await TreasuryFixtures.CreateAsync(_db, "1");

        var draft = await service.CreateVoucherAsync(actor, TreasuryFixtures.Receipt(f,
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

    [Fact]
    public async Task Payment_allocation_settles_invoice_and_rejects_duplicate_or_excess()
    {
        var actor = await _db.AdminActorAsync();
        var service = _db.TreasuryService();
        var f = await TreasuryFixtures.CreateAsync(_db, "2");
        var invoice = await TreasuryFixtures.PostPurchaseInvoiceAsync(_db, f, "2", 300);
        var payment = await TreasuryFixtures.PostPaymentAsync(_db, f, invoice.SupplierId!.Value,
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

        await using (var connection = new NpgsqlConnection(TestDb.AdminConnection))
        {
            await connection.OpenAsync();
            var partyNet = await connection.ExecuteScalarAsync<decimal>(
                "SELECT coalesce(sum(debit - credit), 0) FROM party_balances " +
                "WHERE party_type = 'S' AND party_id = @id", new { id = invoice.SupplierId });
            Assert.Equal(-reloaded.Balance, partyNet);
        }

        var duplicate = await Assert.ThrowsAsync<ValidationAppException>(() =>
            service.AllocateAsync(actor, payment.Id, invoice.Id, 10));
        Assert.Equal("validation.allocation_duplicate", duplicate.Code);

        var smallPayment = await TreasuryFixtures.PostPaymentAsync(_db, f,
            invoice.SupplierId!.Value, invoice.PartyName ?? "مورد", 100);
        var secondInvoice = await TreasuryFixtures.PostPurchaseInvoiceAsync(_db, f, "2b", 300);
        var excess = await Assert.ThrowsAsync<ValidationAppException>(() =>
            service.AllocateAsync(actor, smallPayment.Id, secondInvoice.Id, 150));
        Assert.Equal("validation.allocation_exceeds_voucher", excess.Code);
    }
}
