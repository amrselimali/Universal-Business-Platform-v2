using Dapper;
using Npgsql;
using Ubp.Core.Models;
using Ubp.Core.Results;
using Xunit;

namespace Ubp.Tests;

public sealed class AccountingRulesTests : IClassFixture<TestDb>
{
    private readonly TestDb _db;

    public AccountingRulesTests(TestDb db) => _db = db;

    private static DateTime Date => new(2026, 4, 10);

    [Fact]
    public async Task Receivable_account_requires_a_customer_on_posting()
    {
        var actor = await _db.AdminActorAsync();
        var chart = await _db.SeedAccountingAsync();
        var branch = await _db.CreateBranchAsync("GL-R1", "فرع الأطراف");

        var id = await _db.JournalEntryRepo.CreateDraftAsync(Guid.CreateVersion7(),
            new JournalEntryInput
            {
                JournalId = await _db.JournalIdByCodeAsync("GEN"),
                BranchId = branch,
                EntryDate = Date,
                Lines =
                {
                    new JournalEntryLineInput { AccountId = chart["1110"].Id, Debit = 300m },
                    new JournalEntryLineInput { AccountId = chart["4100"].Id, Credit = 300m }
                }
            }, actor.UserId);

        var exception = await Assert.ThrowsAsync<ValidationAppException>(async () =>
            await _db.JournalEntryService().PostAsync(actor, id));

        Assert.Equal("validation.party_required", exception.Code);
        Assert.Equal("1", exception.Args["line"]);
    }

    [Fact]
    public async Task Cost_center_policy_is_enforced_on_posting()
    {
        var actor = await _db.AdminActorAsync();
        var chart = await _db.SeedAccountingAsync();
        var branch = await _db.CreateBranchAsync("GL-R2", "فرع المراكز");

        await using var connection = new NpgsqlConnection(TestDb.AdminConnection);
        await connection.OpenAsync();
        await connection.ExecuteAsync(
            "UPDATE accounts SET cost_center_policy = 2 WHERE id = @id",
            new { id = chart["5200"].Id });

        var id = await _db.JournalEntryRepo.CreateDraftAsync(Guid.CreateVersion7(),
            new JournalEntryInput
            {
                JournalId = await _db.JournalIdByCodeAsync("GEN"),
                BranchId = branch,
                EntryDate = Date,
                Lines =
                {
                    new JournalEntryLineInput { AccountId = chart["5200"].Id, Debit = 75m },
                    new JournalEntryLineInput { AccountId = chart["1101"].Id, Credit = 75m }
                }
            }, actor.UserId);

        var exception = await Assert.ThrowsAsync<ValidationAppException>(async () =>
            await _db.JournalEntryService().PostAsync(actor, id));
        Assert.Equal("validation.cost_center_required", exception.Code);

        var costCenter = await _db.CostCenterRepo.CreateAsync(Guid.CreateVersion7(),
            new CostCenterInput { Code = "CC-GL", NameAr = "مركز اختبار" }, "/CC-GL/", 1, actor.UserId);

        await connection.ExecuteAsync(
            "UPDATE journal_entry_lines SET cost_center_id = @cc WHERE entry_id = @entry",
            new { cc = costCenter, entry = id });

        var posted = await _db.JournalEntryService().PostAsync(actor, id);
        Assert.Equal(EntryStatuses.Posted, posted.Status);
    }

    [Fact]
    public async Task Closed_period_blocks_accounting_posting()
    {
        var actor = await _db.AdminActorAsync();
        var chart = await _db.SeedAccountingAsync();
        var branch = await _db.CreateBranchAsync("GL-R3", "فرع مغلق");

        var periods = _db.PeriodService();
        var period = await periods.CreateAsync(actor, new FiscalPeriodInput
        {
            Code = "P-GL-2026", NameAr = "فترة 2026",
            StartDate = new DateTime(2026, 1, 1), EndDate = new DateTime(2026, 12, 31)
        });
        await _db.PeriodRepo.UpsertClosureAsync(period.Id, "COMPANY", null, "ACCOUNTING", true,
            actor.UserId, "اختبار");

        var detail = await _db.JournalEntryService().CreateAsync(actor, new JournalEntryInput
        {
            JournalId = await _db.JournalIdByCodeAsync("GEN"),
            BranchId = branch,
            EntryDate = Date,
            Lines =
            {
                new JournalEntryLineInput { AccountId = chart["1101"].Id, Debit = 10m },
                new JournalEntryLineInput { AccountId = chart["4100"].Id, Credit = 10m }
            }
        });

        var exception = await Assert.ThrowsAsync<ValidationAppException>(async () =>
            await _db.JournalEntryService().PostAsync(actor, detail.Header.Id));
        Assert.Equal("validation.period_closed", exception.Code);
    }

    [Fact]
    public async Task Draft_can_be_cancelled_and_deleted_but_not_after_posting()
    {
        var actor = await _db.AdminActorAsync();
        var chart = await _db.SeedAccountingAsync();
        var branch = await _db.CreateBranchAsync("GL-R4", "فرع الإلغاء");

        var detail = await _db.JournalEntryService().CreateAsync(actor, new JournalEntryInput
        {
            JournalId = await _db.JournalIdByCodeAsync("GEN"),
            BranchId = branch,
            EntryDate = Date,
            Lines =
            {
                new JournalEntryLineInput { AccountId = chart["1101"].Id, Debit = 40m },
                new JournalEntryLineInput { AccountId = chart["4100"].Id, Credit = 40m }
            }
        });

        var cancelled = await _db.JournalEntryService().CancelAsync(actor, detail.Header.Id);
        Assert.Equal(EntryStatuses.Cancelled, cancelled.Status);

        var deletePosted = await Assert.ThrowsAsync<ValidationAppException>(async () =>
            await _db.JournalEntryService().DeleteAsync(actor, detail.Header.Id));
        Assert.Equal("validation.entry_immutable", deletePosted.Code);
    }
}
