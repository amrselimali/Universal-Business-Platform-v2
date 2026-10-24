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
        { BranchId = f.Branch, TreasuryId = f.TreasuryId, OpeningCash = 500 });
        Assert.True(first.IsOpen);
        Assert.StartsWith("SHIFT-", first.Code);

        var second = await Assert.ThrowsAsync<ValidationAppException>(() =>
            service.OpenShiftAsync(actor, new ShiftInput
            { BranchId = f.Branch, TreasuryId = f.TreasuryId, OpeningCash = 100 }));
        Assert.Equal("validation.shift_already_open", second.Code);

        var open = await service.GetOpenShiftAsync(actor, f.TreasuryId);
        Assert.Equal(first.Id, open!.Id);
    }

    [Fact]
    public async Task Close_shift_posts_cash_variance_to_over_short_account()
    {
        var actor = await _db.AdminActorAsync();
        var service = _db.TreasuryService();
        var f = await SetupAsync("2");

        var shift = await service.OpenShiftAsync(actor, new ShiftInput
        { BranchId = f.Branch, TreasuryId = f.TreasuryId, OpeningCash = 500 });

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

        var closed = await service.CloseShiftAsync(actor, shift.Id, 690);

        Assert.Equal(ShiftStatuses.Closed, closed.Status);
        Assert.Equal(500m, closed.OpeningCash);
        Assert.Equal(700m, closed.ExpectedCash);
        Assert.Equal(690m, closed.CountedCash);
        Assert.Equal(-10m, closed.Variance);
        Assert.NotNull(closed.ClosedAt);

        await using var connection = new NpgsqlConnection(TestDb.AdminConnection);
        await connection.OpenAsync();
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
}
