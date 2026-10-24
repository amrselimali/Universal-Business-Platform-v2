using Dapper;
using Npgsql;
using Ubp.Core.Models;
using Ubp.Core.Results;
using Xunit;

namespace Ubp.Tests;

public sealed class AccountingJournalTests : IClassFixture<TestDb>
{
    private readonly TestDb _db;

    public AccountingJournalTests(TestDb db) => _db = db;

    private static DateTime Date => new(2026, 3, 15);

    [Fact]
    public async Task Balanced_entry_posts_and_updates_monthly_balances()
    {
        var actor = await _db.AdminActorAsync();
        var chart = await _db.SeedAccountingAsync();
        var branch = await _db.CreateBranchAsync("GL-B1", "فرع الميزان");

        var posted = await _db.PostBalancedEntryAsync(branch, Date,
            chart["1101"].Id, chart["4100"].Id, 1250m);

        Assert.Equal(EntryStatuses.Posted, posted.Status);
        Assert.NotNull(posted.EntryNo);
        Assert.Equal(1250m, posted.TotalDebit);
        Assert.Equal(1250m, posted.TotalCredit);

        var report = await _db.AccountingReports().TrialBalanceAsync(actor,
            new DateTime(2026, 3, 1), new DateTime(2026, 3, 31), branch);

        Assert.True(report.IsBalanced);
        Assert.Equal(1250m, report.TotalDebit);
        Assert.Equal(1250m, report.TotalCredit);
        Assert.Contains(report.Rows, r => r.Code == "1101" && r.Debit == 1250m);
    }

    [Fact]
    public async Task Unbalanced_entry_cannot_be_posted()
    {
        var actor = await _db.AdminActorAsync();
        var chart = await _db.SeedAccountingAsync();
        var branch = await _db.CreateBranchAsync("GL-B2", "فرع غير متوازن");

        var detail = await _db.JournalEntryService().CreateAsync(actor, new JournalEntryInput
        {
            JournalId = await _db.JournalIdByCodeAsync("GEN"),
            BranchId = branch,
            EntryDate = Date,
            Lines =
            {
                new JournalEntryLineInput { AccountId = chart["1101"].Id, Debit = 100m },
                new JournalEntryLineInput { AccountId = chart["4100"].Id, Credit = 90m }
            }
        });

        var exception = await Assert.ThrowsAsync<ValidationAppException>(async () =>
            await _db.JournalEntryService().PostAsync(actor, detail.Header.Id));

        Assert.Equal("validation.entry_not_balanced", exception.Code);
        var reloaded = await _db.JournalEntryRepo.GetAsync(detail.Header.Id);
        Assert.Equal(EntryStatuses.Draft, reloaded!.Status);
    }

    [Fact]
    public async Task Posted_entry_is_immutable_at_database_level()
    {
        var chart = await _db.SeedAccountingAsync();
        var branch = await _db.CreateBranchAsync("GL-B3", "فرع القفل");
        var posted = await _db.PostBalancedEntryAsync(branch, Date,
            chart["1101"].Id, chart["4100"].Id, 500m);

        await using var connection = new NpgsqlConnection(TestDb.AdminConnection);
        await connection.OpenAsync();

        var header = await Assert.ThrowsAsync<PostgresException>(async () =>
            await connection.ExecuteAsync(
                "UPDATE journal_entries SET description = 'تعديل' WHERE id = @id",
                new { id = posted.Id }));
        Assert.Equal("23514", header.SqlState);
        Assert.Contains("JOURNAL_ENTRY_IMMUTABLE", header.MessageText);

        var line = await Assert.ThrowsAsync<PostgresException>(async () =>
            await connection.ExecuteAsync(
                "UPDATE journal_entry_lines SET debit = debit + 1 WHERE entry_id = @id",
                new { id = posted.Id }));
        Assert.Equal("23514", line.SqlState);

        var delete = await Assert.ThrowsAsync<PostgresException>(async () =>
            await connection.ExecuteAsync("DELETE FROM journal_entries WHERE id = @id",
                new { id = posted.Id }));
        Assert.Equal("23514", delete.SqlState);
    }

    [Fact]
    public async Task Reversal_nets_balances_and_cannot_be_repeated()
    {
        var actor = await _db.AdminActorAsync();
        var chart = await _db.SeedAccountingAsync();
        var branch = await _db.CreateBranchAsync("GL-B4", "فرع العكس");
        var posted = await _db.PostBalancedEntryAsync(branch, Date,
            chart["1101"].Id, chart["4100"].Id, 900m);
        var reversal = await _db.JournalEntryService().ReverseAsync(actor, posted.Id, Date);

        Assert.Equal(posted.Id, reversal.ReversalOfId);
        Assert.Equal(EntryStatuses.Posted, reversal.Status);
        Assert.Equal(900m, reversal.TotalDebit);

        var original = await _db.JournalEntryRepo.GetAsync(posted.Id);
        Assert.True(original!.IsReversed);

        var report = await _db.AccountingReports().TrialBalanceAsync(actor,
            new DateTime(2026, 3, 1), new DateTime(2026, 3, 31), branch);
        Assert.True(report.IsBalanced);
        Assert.All(report.Rows, r => Assert.Equal(0m, r.ClosingDebit + r.ClosingCredit));
        var cash = Assert.Single(report.Rows, r => r.Code == "1101");
        Assert.Equal(900m, cash.Debit);
        Assert.Equal(900m, cash.Credit);

        var again = await Assert.ThrowsAsync<ValidationAppException>(async () =>
            await _db.JournalEntryService().ReverseAsync(actor, posted.Id, Date));
        Assert.Equal("validation.entry_already_reversed", again.Code);

        await using var connection = new NpgsqlConnection(TestDb.AdminConnection);
        await connection.OpenAsync();
        var atDatabase = await Assert.ThrowsAsync<PostgresException>(async () =>
            await connection.ExecuteScalarAsync<Guid>(
                "SELECT fn_gl_reverse_entry(@entry, @user, @date::date)",
                new { entry = posted.Id, user = actor.UserId, date = Date }));
        Assert.Equal("23514", atDatabase.SqlState);
        Assert.Contains("ENTRY_ALREADY_REVERSED", atDatabase.MessageText);
    }
}
