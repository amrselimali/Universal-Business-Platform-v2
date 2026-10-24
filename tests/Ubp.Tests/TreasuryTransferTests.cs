using Dapper;
using Npgsql;
using Ubp.Core.Models;
using Xunit;

namespace Ubp.Tests;

public sealed class TreasuryTransferTests : IClassFixture<TestDb>
{
    private readonly TestDb _db;

    public TreasuryTransferTests(TestDb db) => _db = db;

    [Fact]
    public async Task Transfer_between_branches_clears_through_inter_branch_account()
    {
        var actor = await _db.AdminActorAsync();
        var service = _db.TreasuryService();
        var f = await TreasuryFixtures.CreateAsync(_db, "3");

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
        var f = await TreasuryFixtures.CreateAsync(_db, "4");

        var draft = await service.CreateVoucherAsync(actor, TreasuryFixtures.Receipt(f,
            new VoucherLineInput { TreasuryMethodId = f.CashMethod, Amount = 100 }));
        var deletable = await service.CreateVoucherAsync(actor, TreasuryFixtures.Receipt(f,
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
