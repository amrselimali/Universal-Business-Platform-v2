using Ubp.Core.Security;
using Xunit;

namespace Ubp.Tests;

/// <summary>
/// 8ج: تقارير «فرع/عدة فروع/كل الفروع» — ميزان المراجعة وكشف الطرف ومراكز التكلفة
/// تعمل على مصفوفة فروع مع توافق خلفي كامل مع توقيع الفرع الواحد.
/// </summary>
public sealed class MultiBranchReportsTests : IClassFixture<TestDb>
{
    private readonly TestDb _db;

    public MultiBranchReportsTests(TestDb db) => _db = db;

    [Fact]
    public async Task Trial_balance_sums_the_requested_branches_and_filters_a_subset()
    {
        var actor = await _db.AdminActorAsync();
        var chart = await _db.SeedAccountingAsync();
        var first = await _db.CreateBranchAsync("MB-B1", "فرع أول");
        var second = await _db.CreateBranchAsync("MB-B2", "فرع ثاني");
        var today = DateTime.Today;

        await _db.PostBalancedEntryAsync(first, today, chart["1101"].Id, chart["4100"].Id, 100);
        await _db.PostBalancedEntryAsync(second, today, chart["1101"].Id, chart["4100"].Id, 250);

        var reports = _db.AccountingReports();
        var both = await reports.TrialBalanceForBranchesAsync(actor, today, today,
            new[] { first, second });
        var onlyFirst = await reports.TrialBalanceForBranchesAsync(actor, today, today,
            new[] { first });

        var cashBoth = both.Rows.Single(r => r.Code == "1101");
        var cashFirst = onlyFirst.Rows.Single(r => r.Code == "1101");

        Assert.Equal(350m, cashBoth.Debit);
        Assert.Equal(100m, cashFirst.Debit);
        Assert.Equal(new[] { first, second }, both.BranchIds.OrderBy(x => x == second));
        Assert.Equal(250m, both.Rows.Single(r => r.Code == "4100").Credit
                           - onlyFirst.Rows.Single(r => r.Code == "4100").Credit);
    }

    [Fact]
    public async Task Empty_scope_returns_all_visible_branches_and_single_branch_signature_still_works()
    {
        var actor = await _db.AdminActorAsync();
        var chart = await _db.SeedAccountingAsync();
        var branch = await _db.CreateBranchAsync("MB-B3", "فرع ثالث");
        var today = DateTime.Today;
        await _db.PostBalancedEntryAsync(branch, today, chart["1101"].Id, chart["4100"].Id, 40);

        var reports = _db.AccountingReports();

        // التوقيع القديم (Guid?) ما زال يعمل
        var legacy = await reports.TrialBalanceAsync(actor, today, today, branch);
        Assert.Equal(40m, legacy.Rows.Single(r => r.Code == "1101").Debit);

        // مصفوفة فارغة = كل فروع المستخدم (المدير العام: الكل)
        var all = await reports.TrialBalanceForBranchesAsync(actor, today, today, Array.Empty<Guid>());
        Assert.Empty(all.BranchIds);
        Assert.True(all.Rows.Single(r => r.Code == "1101").Debit >= 40m);
    }

    [Fact]
    public async Task Party_statement_and_cost_centers_accept_the_branch_array()
    {
        var actor = await _db.AdminActorAsync();
        var chart = await _db.SeedAccountingAsync();
        var branch = await _db.CreateBranchAsync("MB-B4", "فرع رابع");
        var customer = await _db.PartnerRepo.CreateAsync(Ubp.Core.Models.PartnerKind.Customer,
            new Ubp.Core.Models.PartnerInput { Code = "MB-C1", NameAr = "عميل التقارير" });
        var today = DateTime.Today;

        await _db.PostBalancedEntryAsync(branch, today, chart["1110"].Id, chart["4100"].Id, 60,
            customerId: customer.Id);

        var reports = _db.AccountingReports();
        var statement = await reports.PartyStatementForBranchesAsync(actor, 'C', customer.Id,
            today, today, new[] { branch });
        var centers = await reports.CostCenterForBranchesAsync(actor, today, today, new[] { branch });

        Assert.NotNull(statement);
        Assert.False(string.IsNullOrWhiteSpace(statement.PartyName));
        Assert.NotNull(centers);
    }
}
