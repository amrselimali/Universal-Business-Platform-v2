using Dapper;
using Npgsql;
using Ubp.Core.Models;
using Ubp.Core.Results;
using Ubp.Core.Security;
using Xunit;

namespace Ubp.Tests;

public sealed class TreasuryShiftTests : IClassFixture<TestDb>
{
    private readonly TestDb _db;

    public TreasuryShiftTests(TestDb db) => _db = db;

    private sealed record Fixture(Guid Branch, Guid TreasuryId, Guid CashMethod, Partner Customer,
        Partner Supplier, Dictionary<string, Account> Chart);

    private async Task<Fixture> SetupAsync(string suffix)
    {
        var actor = await _db.AdminActorAsync();
        var chart = await _db.SeedAccountingAsync();
        var settings = _db.AccountingSettings();
        await settings.SetDefaultAsync(actor, PostingKeys.CustomerAdvances, chart["1110"].Id);
        await settings.SetDefaultAsync(actor, PostingKeys.SupplierAdvances, chart["2100"].Id);
        await settings.SetDefaultAsync(actor, PostingKeys.CashOverShort, chart["5490"].Id);

        var branch = await _db.CreateBranchAsync("TS-B-" + suffix, "فرع وردية " + suffix);
        var treasuryId = Guid.CreateVersion7();
        await _db.TreasuryRepo.CreateTreasuryAsync(treasuryId, new TreasuryInput
        {
            BranchId = branch, Code = "TS-T-" + suffix, NameAr = "خزينة وردية " + suffix,
            TreasuryType = TreasuryTypes.Cash, AccountId = chart["1101"].Id
        }, actor.UserId);

        var cash = Guid.CreateVersion7();
        await _db.TreasuryRepo.CreateMethodAsync(cash, new TreasuryMethodInput
        {
            TreasuryId = treasuryId, Code = "CASH", NameAr = "نقدي",
            MethodType = PaymentMethodTypes.Cash
        }, actor.UserId);

        var customer = await _db.PartnerRepo.CreateAsync(PartnerKind.Customer, new PartnerInput
        { Code = "TS-C-" + suffix, NameAr = "عميل وردية " + suffix });
        var supplier = await _db.PartnerRepo.CreateAsync(PartnerKind.Supplier, new PartnerInput
        { Code = "TS-S-" + suffix, NameAr = "مورد وردية " + suffix });

        return new Fixture(branch, treasuryId, cash, customer, supplier, chart);
    }

    private async Task PostVoucherAsync(Fixture f, string voucherType, Partner party,
        decimal amount)
    {
        var actor = await _db.AdminActorAsync();
        var service = _db.TreasuryService();
        var draft = await service.CreateVoucherAsync(actor, new VoucherInput
        {
            VoucherType = voucherType, BranchId = f.Branch, DocDate = DateTime.Today,
            PartyType = voucherType == VoucherTypes.Receipt ? "C" : "S",
            PartyId = party.Id, PartyName = party.NameAr, TreasuryId = f.TreasuryId,
            Lines = { new VoucherLineInput { TreasuryMethodId = f.CashMethod, Amount = amount } }
        });
        await service.PostVoucherAsync(actor, draft.Id);
    }

    [Fact]
    public async Task Second_open_shift_on_same_treasury_is_rejected()
    {
        var actor = await _db.AdminActorAsync();
        var service = _db.TreasuryService();
        var f = await SetupAsync("1");

        var first = await service.OpenShiftAsync(actor, new ShiftInput
        { BranchId = f.Branch });
        Assert.True(first.IsOpen);
        Assert.StartsWith("SHIFT-", first.Code);

        var second = await Assert.ThrowsAsync<ValidationAppException>(() =>
            service.OpenShiftAsync(actor, new ShiftInput { BranchId = f.Branch }));
        Assert.Equal("validation.shift_already_open", second.Code);

        var open = await service.GetOpenShiftAsync(actor, f.Branch);
        Assert.Equal(first.Id, open!.Id);
    }

    [Fact]
    public async Task Close_shift_posts_cash_variance_to_over_short_account()
    {
        var actor = await _db.AdminActorAsync();
        var service = _db.TreasuryService();
        var f = await SetupAsync("2");

        var shift = await service.OpenShiftAsync(actor, new ShiftInput
        { BranchId = f.Branch });

        await PostVoucherAsync(f, VoucherTypes.Receipt, f.Customer, 300);
        await PostVoucherAsync(f, VoucherTypes.Payment, f.Supplier, 100);

        await service.SaveDenominationsAsync(actor, shift.Id, new List<ShiftDenominationInput>
        {
            new() { Denomination = 200, Count = 3 },
            new() { Denomination = 50, Count = 2 }
        });
        var denominations = await service.ListDenominationsAsync(actor, shift.Id);
        Assert.Equal(2, denominations.Count);
        Assert.Equal(700m, denominations.Sum(d => d.Total));

        var closed = await service.CloseShiftAsync(actor, shift.Id, new List<ShiftCashCountInput>
        { new() { TreasuryId = f.TreasuryId, CountedCash = 190 } });

        Assert.Equal(ShiftStatuses.Closed, closed.Status);
        Assert.Equal(0m, closed.OpeningCash);
        Assert.Equal(200m, closed.ExpectedCash);
        Assert.Equal(190m, closed.CountedCash);
        Assert.Equal(-10m, closed.Variance);
        Assert.NotNull(closed.ClosedAt);

        await using var connection = new NpgsqlConnection(TestDb.AdminConnection);
        await connection.OpenAsync();
        // عدّ النقدية يُحفظ مسودة عند الإغلاق ولا يُرحَّل أي فرق قبل اعتماد المحاسب.
        var draftCounts = await service.ListCashCountsAsync(actor, shift.Id);
        var draft = Assert.Single(draftCounts);
        Assert.Equal(ShiftCashCountStatuses.Draft, draft.Status);
        Assert.Equal(-10m, draft.Variance);
        Assert.Null(await connection.ExecuteScalarAsync<Guid?>(
            "SELECT id FROM journal_entries WHERE source_type = 'cash_shift' AND reference = @code",
            new { code = shift.Code }));

        await service.ReviewShiftAsync(actor, shift.Id);

        var entryId = await connection.ExecuteScalarAsync<Guid?>(
            "SELECT id FROM journal_entries WHERE source_type = 'cash_shift' AND reference = @code",
            new { code = shift.Code });
        Assert.NotNull(entryId);

        var lines = (await connection.QueryAsync<(string Code, decimal Debit, decimal Credit)>("""
            SELECT a.code, l.debit, l.credit FROM journal_entry_lines l
            JOIN accounts a ON a.id = l.account_id WHERE l.entry_id = @id ORDER BY a.code
            """, new { id = entryId })).ToList();
        Assert.Equal(10m, lines.Single(x => x.Code == "5490").Debit);
        Assert.Equal(10m, lines.Single(x => x.Code == "1101").Credit);
    }

    [Fact]
    public async Task Opening_balances_are_computed_from_branch_accounts_and_locked()
    {
        var actor = await _db.AdminActorAsync();
        var service = _db.TreasuryService();
        var f = await SetupAsync("3");

        await PostVoucherAsync(f, VoucherTypes.Receipt, f.Customer, 400);

        var balances = await service.GetOpeningBalancesAsync(actor, f.Branch);
        Assert.Equal(400m, balances.CarriedFromAccounts);
        Assert.Equal(0m, balances.PendingReview);
        Assert.Equal(0m, balances.MeterBalance);
        Assert.Equal(400m, balances.OpeningCash);

        var shift = await service.OpenShiftAsync(actor, new ShiftInput { BranchId = f.Branch });
        Assert.Equal(400m, shift.CarriedBalance);
        Assert.Equal(400m, shift.OpeningCash);
        Assert.Null(shift.TreasuryId);
    }

    [Fact]
    public async Task Closing_shift_submits_drafts_for_accountant_review()
    {
        var actor = await _db.AdminActorAsync();
        var service = _db.TreasuryService();
        var f = await SetupAsync("4");

        var shift = await service.OpenShiftAsync(actor, new ShiftInput { BranchId = f.Branch });
        var draft = await service.CreateVoucherAsync(actor, new VoucherInput
        {
            VoucherType = VoucherTypes.Receipt, BranchId = f.Branch, DocDate = DateTime.Today,
            PartyType = "C", PartyId = f.Customer.Id, PartyName = f.Customer.NameAr,
            TreasuryId = f.TreasuryId,
            Lines = { new VoucherLineInput { TreasuryMethodId = f.CashMethod, Amount = 75 } }
        });

        var closed = await service.CloseShiftAsync(actor, shift.Id);
        Assert.Equal(ShiftReviewStatuses.Pending, closed.ReviewStatus);

        var report = await service.GetShiftReportAsync(actor, shift.Id);
        var line = Assert.Single(report.Lines);
        Assert.Equal(75m, line.Collected);
        Assert.Equal(75m, report.CollectionsTotal);
        Assert.NotNull(draft.Id);

        var reviewed = await service.ReviewShiftAsync(actor, shift.Id);
        Assert.Equal(ShiftReviewStatuses.Reviewed, reviewed.ReviewStatus);
    }

    [Fact]
    public async Task Next_shift_opening_includes_previous_shift_drafts_under_review()
    {
        var actor = await _db.AdminActorAsync();
        var service = _db.TreasuryService();
        var f = await SetupAsync("6");

        var first = await service.OpenShiftAsync(actor, new ShiftInput { BranchId = f.Branch });
        await service.CreateVoucherAsync(actor, new VoucherInput
        {
            VoucherType = VoucherTypes.Receipt, BranchId = f.Branch, DocDate = DateTime.Today,
            PartyType = "C", PartyId = f.Customer.Id, PartyName = f.Customer.NameAr,
            TreasuryId = f.TreasuryId,
            Lines = { new VoucherLineInput { TreasuryMethodId = f.CashMethod, Amount = 75 } }
        });
        await service.CloseShiftAsync(actor, first.Id);

        var balances = await service.GetOpeningBalancesAsync(actor, f.Branch);
        Assert.Equal(75m, balances.PendingReview);
        Assert.Equal(1, balances.PendingReviewShifts);
        Assert.Equal(75m, balances.OpeningCash);

        var second = await service.OpenShiftAsync(actor, new ShiftInput { BranchId = f.Branch });
        Assert.Equal(75m, second.ReviewBalance);
        Assert.Equal(75m, second.OpeningCash);
    }

    [Fact]
    public async Task Auto_close_closes_open_shifts_that_passed_five_am()
    {
        var actor = await _db.AdminActorAsync();
        var service = _db.TreasuryService();
        var f = await SetupAsync("5");

        var shift = await service.OpenShiftAsync(actor, new ShiftInput { BranchId = f.Branch });
        await PostVoucherAsync(f, VoucherTypes.Receipt, f.Customer, 120);

        var due = await _db.TreasuryRepo.ListShiftsDueForAutoCloseAsync(
            DateTimeOffset.UtcNow.AddHours(1).ToOffset(TimeSpan.FromHours(2)));
        Assert.Contains(due, s => s.Id == shift.Id);

        var closed = await service.AutoCloseAsync(DateTimeOffset.UtcNow.AddHours(1));
        Assert.True(closed >= 1);

        var reloaded = await _db.TreasuryRepo.GetShiftAsync(shift.Id);
        Assert.NotNull(reloaded);
        Assert.Equal(ShiftStatuses.Closed, reloaded!.Status);
        Assert.True(reloaded.AutoClosed);
        Assert.Equal(reloaded.ExpectedCash, reloaded.CountedCash);
        Assert.Equal(0m, reloaded.Variance);
    }
}
