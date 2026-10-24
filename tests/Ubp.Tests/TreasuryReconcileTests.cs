using Dapper;
using Npgsql;
using Ubp.Core.Models;
using Ubp.Core.Results;
using Xunit;

namespace Ubp.Tests;

public sealed class TreasuryReconcileTests : IClassFixture<TestDb>
{
    private readonly TestDb _db;

    public TreasuryReconcileTests(TestDb db) => _db = db;

    [Fact]
    public async Task Clearing_settlement_posts_balanced_entry_and_zeroes_account()
    {
        var actor = await _db.AdminActorAsync();
        var service = _db.TreasuryService();
        var f = await TreasuryFixtures.CreateAsync(_db, "R1");

        var draft = await service.CreateVoucherAsync(actor, TreasuryFixtures.Receipt(f,
            new VoucherLineInput { TreasuryMethodId = f.CardMethod, Amount = 500 }));
        await service.PostVoucherAsync(actor, draft.Id);

        var before = await service.ListClearingBalancesAsync(actor, f.Branch);
        var clearing = before.Single(x => x.AccountId == f.Chart["1130"].Id);
        Assert.Equal(490m, clearing.Balance);

        var entryId = await service.SettleClearingAsync(actor, f.TreasuryId, clearing.AccountId,
            490m, DateTime.Today, "SETTLE-1");

        var after = await service.ListClearingBalancesAsync(actor, f.Branch);
        Assert.Equal(0m, after.Single(x => x.AccountId == clearing.AccountId).Balance);

        await using var connection = new NpgsqlConnection(TestDb.AdminConnection);
        await connection.OpenAsync();
        var entry = await connection.QuerySingleAsync<(decimal Debit, decimal Credit, string Status)>(
            "SELECT total_debit, total_credit, status FROM journal_entries WHERE id = @id",
            new { id = entryId });
        Assert.Equal(490m, entry.Debit);
        Assert.Equal(entry.Debit, entry.Credit);
        Assert.Equal("posted", entry.Status);
    }

    [Fact]
    public async Task Draft_voucher_can_be_cancelled_and_posted_voucher_cannot()
    {
        var actor = await _db.AdminActorAsync();
        var service = _db.TreasuryService();
        var f = await TreasuryFixtures.CreateAsync(_db, "R2");

        var draft = await service.CreateVoucherAsync(actor, TreasuryFixtures.Receipt(f,
            new VoucherLineInput { TreasuryMethodId = f.CashMethod, Amount = 100 }));
        await service.CancelVoucherAsync(actor, draft.Id);
        Assert.Equal(VoucherStatuses.Cancelled,
            (await _db.TreasuryRepo.GetVoucherAsync(draft.Id))!.Status);
        await Assert.ThrowsAsync<ValidationAppException>(() =>
            service.PostVoucherAsync(actor, draft.Id));

        var postedDraft = await service.CreateVoucherAsync(actor, TreasuryFixtures.Receipt(f,
            new VoucherLineInput { TreasuryMethodId = f.CashMethod, Amount = 50 }));
        var posted = await service.PostVoucherAsync(actor, postedDraft.Id);
        var cancelPosted = await Assert.ThrowsAsync<ValidationAppException>(() =>
            service.CancelVoucherAsync(actor, posted.Id));
        Assert.Equal("validation.voucher_not_draft", cancelPosted.Code);
    }

    [Fact]
    public async Task Reopen_shift_reverses_variance_and_allows_reclose()
    {
        var actor = await _db.AdminActorAsync();
        var service = _db.TreasuryService();
        var f = await TreasuryFixtures.CreateAsync(_db, "R3");

        var shift = await service.OpenShiftAsync(actor, new ShiftInput
        { BranchId = f.Branch, TreasuryId = f.TreasuryId, OpeningCash = 500 });

        var receipt = await service.CreateVoucherAsync(actor, TreasuryFixtures.Receipt(f,
            new VoucherLineInput { TreasuryMethodId = f.CashMethod, Amount = 300 }));
        await service.PostVoucherAsync(actor, receipt.Id);

        var closed = await service.CloseShiftAsync(actor, shift.Id, 790);
        Assert.Equal(-10m, closed.Variance);

        var reopened = await service.ReopenShiftAsync(actor, shift.Id, "إعادة عدّ النقد");
        Assert.True(reopened.IsOpen);
        Assert.Null(reopened.Variance);
        Assert.Equal(ShiftStatuses.Open, reopened.Status);

        await using (var connection = new NpgsqlConnection(TestDb.AdminConnection))
        {
            await connection.OpenAsync();
            var entries = (await connection.QueryAsync<(Guid Id, Guid? ReversalOf, bool IsReversed)>(
                "SELECT id, reversal_of_id, is_reversed FROM journal_entries " +
                "WHERE source_type = 'cash_shift' AND reference = @code",
                new { code = shift.Code })).ToList();
            Assert.Equal(2, entries.Count);
            var original = entries.Single(e => e.ReversalOf is null);
            Assert.True(original.IsReversed);
        }

        var reclosed = await service.CloseShiftAsync(actor, shift.Id, 800);
        Assert.Equal(ShiftStatuses.Closed, reclosed.Status);
        Assert.Equal(0m, reclosed.Variance);
    }

    [Fact]
    public async Task Allocation_action_lists_only_open_invoices()
    {
        var actor = await _db.AdminActorAsync();
        var service = _db.TreasuryService();
        var f = await TreasuryFixtures.CreateAsync(_db, "R4");

        var invoice = await TreasuryFixtures.PostPurchaseInvoiceAsync(_db, f, "R4", 300);
        var payment = await TreasuryFixtures.PostPaymentAsync(_db, f, invoice.SupplierId!.Value,
            invoice.PartyName ?? "مورد", 250);

        var open = await service.ListOpenDocumentsAsync(actor, payment.Id);
        Assert.Single(open);
        Assert.Equal(300m, open[0].Balance);

        await service.AllocateAsync(actor, payment.Id, invoice.Id, 250);
        var after = await service.ListOpenDocumentsAsync(actor, payment.Id);
        Assert.Equal(50m, after.Single().Balance);
    }
}
