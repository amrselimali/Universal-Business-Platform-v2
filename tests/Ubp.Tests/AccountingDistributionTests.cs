using Ubp.Core.Models;
using Ubp.Core.Results;
using Xunit;

namespace Ubp.Tests;

public sealed class AccountingDistributionTests : IClassFixture<TestDb>
{
    private readonly TestDb _db;

    public AccountingDistributionTests(TestDb db) => _db = db;

    private static DateTime Date => new(2026, 9, 1);

    [Fact]
    public async Task Cost_distribution_splits_the_line_and_puts_rounding_on_the_last_row()
    {
        var actor = await _db.AdminActorAsync();
        var chart = await _db.SeedAccountingAsync();
        var branch = await _db.CreateBranchAsync("GL-D1", "فرع التوزيع");

        var c1 = await _db.CostCenterRepo.CreateAsync(Guid.CreateVersion7(),
            new CostCenterInput { Code = "CC-1", NameAr = "مركز 1" }, "/CC-1/", 1, actor.UserId);
        var c2 = await _db.CostCenterRepo.CreateAsync(Guid.CreateVersion7(),
            new CostCenterInput { Code = "CC-2", NameAr = "مركز 2" }, "/CC-2/", 1, actor.UserId);
        var c3 = await _db.CostCenterRepo.CreateAsync(Guid.CreateVersion7(),
            new CostCenterInput { Code = "CC-3", NameAr = "مركز 3" }, "/CC-3/", 1, actor.UserId);

        var lines = new List<JournalEntryLineInput>
        {
            new() { AccountId = chart["5200"].Id, Debit = 100m },
            new() { AccountId = chart["1101"].Id, Credit = 100m }
        };

        await _db.JournalEntryService().DistributeCostAsync(actor, lines, 0, new[]
        {
            new CostDistributionLine { CostCenterId = c1, Percentage = 33.33m },
            new CostDistributionLine { CostCenterId = c2, Percentage = 33.33m },
            new CostDistributionLine { CostCenterId = c3, Percentage = 33.34m }
        });

        Assert.Equal(4, lines.Count);
        Assert.Equal(100m, lines.Where(l => l.Debit > 0).Sum(l => l.Debit));
        Assert.Equal(33.33m, lines[0].Debit);
        Assert.Equal(33.33m, lines[1].Debit);
        Assert.Equal(33.34m, lines[2].Debit);
        Assert.Equal(c3, lines[2].CostCenterId);

        var bad = await Assert.ThrowsAsync<ValidationAppException>(async () =>
            await _db.JournalEntryService().DistributeCostAsync(actor, lines, 3, new[]
            {
                new CostDistributionLine { CostCenterId = c1, Percentage = 60m }
            }));
        Assert.Equal("validation.distribution_total", bad.Code);
    }

    [Fact]
    public async Task Opening_balances_are_saved_balanced_and_posted_once()
    {
        var actor = await _db.AdminActorAsync();
        var chart = await _db.SeedAccountingAsync();
        var branch = await _db.CreateBranchAsync("GL-O1", "فرع الافتتاحي");

        var lines = new List<JournalEntryLineInput>
        {
            new() { AccountId = chart["1101"].Id, Debit = 5000m },
            new() { AccountId = chart["1120"].Id, Debit = 2000m }
        };
        await _db.OpeningBalanceService().AutoBalanceEquityAsync(actor, lines, null);
        Assert.Equal(7000m, lines.Sum(l => l.Debit));
        Assert.Equal(7000m, lines.Sum(l => l.Credit));

        var detail = await _db.OpeningBalanceService().SaveDraftAsync(actor, new OpeningBalanceInput
        {
            BranchId = branch, EntryDate = Date, Description = "افتتاحي اختبار", Lines = lines
        });

        Assert.Equal(EntrySources.Opening, detail.Header.SourceType);
        await _db.OpeningBalanceService().PostAsync(actor, detail.Header.Id);

        var report = await _db.AccountingReports().TrialBalanceAsync(actor, Date, Date, branch);
        Assert.True(report.IsBalanced);
        Assert.Equal(7000m, report.TotalDebit);

        var draft = await _db.OpeningBalanceService().GetDraftAsync(actor, branch);
        Assert.Null(draft);
    }

    [Fact]
    public async Task Opening_csv_reports_unknown_accounts_and_missing_parties()
    {
        var actor = await _db.AdminActorAsync();
        var chart = await _db.SeedAccountingAsync();

        var csv = string.Join('\n',
            "code,debit,credit,description,party_code,cost_center_code",
            "1101,100,0,نقدية,,",
            "9999,50,0,حساب غير موجود,,",
            "1110,200,0,عميل بلا كود,,",
            "4100,0,300,إيرادات,,");

        var result = await _db.OpeningBalanceService().ImportCsvAsync(actor, csv);

        Assert.Equal(2, result.Lines.Count);
        Assert.Equal(2, result.Errors.Count);
        Assert.Contains(result.Errors, e => e.Key == "validation.account_not_found" && e.Detail == "9999");
        Assert.Contains(result.Errors, e => e.Key == "validation.party_required" && e.Row == 4);
        Assert.Equal(100m, result.TotalDebit);
        Assert.Equal(300m, result.TotalCredit);
        Assert.False(result.IsBalanced);

        var customer = await _db.PartnerRepo.CreateAsync(PartnerKind.Customer,
            new PartnerInput { Code = "C-O1", NameAr = "عميل افتتاحي" });
        var withParty = await _db.OpeningBalanceService().ImportCsvAsync(actor,
            $"1110,200,0,عميل,C-O1,\n4100,0,200,إيرادات,,");
        Assert.Empty(withParty.Errors);
        Assert.Equal(customer.Id, withParty.Lines[0].CustomerId);
    }
}
