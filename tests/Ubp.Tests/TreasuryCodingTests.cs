using Dapper;
using Npgsql;
using Ubp.Core.Models;
using Ubp.Core.Results;
using Xunit;

namespace Ubp.Tests;

/// <summary>
/// تكويد الخزائن — 0093: الخزائن هي طرق الدفع. يغطي التعديل والأرشفة ومزامنة الطريقة
/// الافتراضية، ومصروفاً تشغيلياً بلا حساب/طريقة، وتسليم نقدية بالحساب الوسيط الافتراضي.
/// </summary>
public sealed class TreasuryCodingTests : IClassFixture<TestDb>
{
    private readonly TestDb _db;

    public TreasuryCodingTests(TestDb db) => _db = db;

    private sealed record Fixture(Guid Branch, Guid TreasuryId, Guid MethodId,
        Dictionary<string, Account> Chart);

    private async Task<Fixture> SetupAsync(string suffix)
    {
        var actor = await _db.AdminActorAsync();
        var chart = await _db.SeedAccountingAsync();
        await TreasuryFixtures.MapDefaultsAsync(_db, actor, chart);

        var branch = await _db.CreateBranchAsync("TC-B-" + suffix, "فرع تكويد " + suffix);
        var treasuryId = Guid.CreateVersion7();
        await _db.TreasuryRepo.CreateTreasuryAsync(treasuryId, new TreasuryInput
        {
            BranchId = branch, Code = "TC-T-" + suffix, NameAr = "خزينة تكويد " + suffix,
            TreasuryType = TreasuryTypes.Cash, AccountId = chart["1101"].Id
        }, actor.UserId);
        var methodId = Guid.CreateVersion7();
        await _db.TreasuryRepo.CreateMethodAsync(methodId, new TreasuryMethodInput
        {
            TreasuryId = treasuryId, Code = "CASH", NameAr = "نقدي"
        }, actor.UserId);

        return new Fixture(branch, treasuryId, methodId, chart);
    }

    [Fact]
    public async Task Editing_a_treasury_updates_it_and_its_default_method()
    {
        var actor = await _db.AdminActorAsync();
        var service = _db.TreasuryService();
        var f = await SetupAsync("1");

        await service.UpdateTreasuryAsync(actor, f.TreasuryId, new TreasuryInput
        {
            BranchId = f.Branch, Code = "TC-T-1", NameAr = "خزينة معدّلة",
            TreasuryType = TreasuryTypes.Bank, AccountId = f.Chart["1101"].Id,
            ProviderFeePercent = 1.5m, ProviderFeeAmount = 2m,
            ProviderFeeAccountId = f.Chart["5200"].Id, FollowInShift = false
        });

        var treasury = (await service.ListTreasuriesAsync(actor, f.Branch))
            .Single(t => t.Id == f.TreasuryId);
        Assert.Equal("خزينة معدّلة", treasury.NameAr);
        Assert.Equal(TreasuryTypes.Bank, treasury.TreasuryType);
        Assert.Equal(1.5m, treasury.ProviderFeePercent);
        Assert.Equal(2m, treasury.ProviderFeeAmount);
        Assert.False(treasury.FollowInShift);

        var methods = await service.ListMethodsAsync(actor, f.TreasuryId);
        Assert.Contains(methods, m => m.Code == "DEFAULT" && m.NameAr == "خزينة معدّلة"
                                     && m.FeePercent == 1.5m && m.FeeFixed == 2m
                                     && m.FollowInShift == false);
    }

    [Fact]
    public async Task Archiving_hides_the_treasury_and_restoring_brings_it_back()
    {
        var actor = await _db.AdminActorAsync();
        var service = _db.TreasuryService();
        var f = await SetupAsync("2");

        await service.ArchiveTreasuryAsync(actor, f.TreasuryId);
        Assert.DoesNotContain(await service.ListTreasuriesAsync(actor, f.Branch),
            t => t.Id == f.TreasuryId);

        var archived = await service.ListTreasuriesAsync(actor, f.Branch, includeArchived: true);
        Assert.Contains(archived, t => t.Id == f.TreasuryId && !t.IsActive);

        await service.RestoreTreasuryAsync(actor, f.TreasuryId);
        Assert.Contains(await service.ListTreasuriesAsync(actor, f.Branch),
            t => t.Id == f.TreasuryId);
    }

    [Fact]
    public async Task Expense_without_account_or_method_uses_the_accounting_defaults()
    {
        var actor = await _db.AdminActorAsync();
        var f = await SetupAsync("3");
        await _db.AccountingSettings().SetDefaultAsync(actor, PostingKeys.OperatingExpense,
            f.Chart["5300"].Id);

        var supplier = await _db.PartnerRepo.CreateAsync(PartnerKind.Supplier, new PartnerInput
        { Code = "TC-S-3", NameAr = "مورد تكويد 3" });

        var created = await _db.OperatingExpenses().CreateAsync(actor, new OperatingExpenseInput
        {
            BranchId = f.Branch, DocDate = DateTime.Today, SupplierId = supplier.Id,
            TreasuryId = f.TreasuryId,
            Lines = { new OperatingExpenseLineInput { Description = "كهرباء", UnitPrice = 120 } }
        });

        var saved = await _db.OperatingExpenses().GetAsync(actor, created.Id);
        var line = Assert.Single(saved!.Lines);
        Assert.Equal(f.Chart["5300"].Id, line.AccountId);
        Assert.NotNull(saved.TreasuryMethodId);

        var posted = await _db.OperatingExpenses().PostAsync(actor, created.Id);
        Assert.True(posted.IsPaid);
        Assert.NotNull(posted.VoucherId);
    }

    [Fact]
    public async Task Cash_handover_uses_the_default_clearing_and_commission_accounts()
    {
        var actor = await _db.AdminActorAsync();
        var service = _db.TreasuryService();
        var f = await SetupAsync("4");
        await _db.AccountingSettings().SetDefaultAsync(actor, PostingKeys.CashClearing,
            f.Chart["1130"].Id);
        await _db.AccountingSettings().SetDefaultAsync(actor,
            PostingKeys.CashHandoverCommission, f.Chart["5200"].Id);

        await service.CreateCashHandoverAsync(actor, new CashHandoverInput
        {
            BranchId = f.Branch, TreasuryId = f.TreasuryId, ClearingAccountId = Guid.Empty,
            Amount = 100, CommissionAmount = 2, DocDate = DateTime.Today
        });

        var handover = Assert.Single(await _db.TreasuryRepo.ListCashHandoversAsync(f.Branch,
            null, null, 10));
        Assert.Equal(f.Chart["1130"].Id, handover.ClearingAccountId);
        Assert.Equal(2m, handover.CommissionAmount);

        await using var connection = new NpgsqlConnection(TestDb.AdminConnection);
        await connection.OpenAsync();
        var entries = (await connection.QueryAsync<(string Code, decimal Debit, decimal Credit)>("""
            SELECT a.code, l.debit, l.credit FROM journal_entry_lines l
            JOIN accounts a ON a.id = l.account_id
            WHERE l.entry_id = (SELECT journal_entry_id FROM cash_handovers WHERE id = @id)
            """, new { id = handover.Id })).ToList();

        Assert.Contains(entries, l => l.Code == "1130" && l.Debit == 100m);
        Assert.Contains(entries, l => l.Code == "5200" && l.Debit == 2m);
        Assert.Equal(102m, entries.Where(l => l.Code == "1101").Sum(l => l.Credit));

        // عمولة التسليم مسموحة فقط للخزائن المتابَعة في الشفت — 0093.
        await service.SetTreasuryFollowInShiftAsync(actor, f.TreasuryId, false);
        var error = await Assert.ThrowsAsync<ValidationAppException>(() =>
            service.CreateCashHandoverAsync(actor, new CashHandoverInput
            {
                BranchId = f.Branch, TreasuryId = f.TreasuryId, ClearingAccountId = Guid.Empty,
                Amount = 50, CommissionAmount = 1, DocDate = DateTime.Today
            }));
        Assert.Equal("validation.handover_commission_requires_shift", error.Code);
    }
}
