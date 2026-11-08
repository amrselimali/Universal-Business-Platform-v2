using Dapper;
using Npgsql;
using Ubp.Core.Models;
using Xunit;

namespace Ubp.Tests;

/// <summary>
/// أرصدة فتح الشفت: متابعة طرق الدفع في الشفت، عدادات الأجهزة لكل جهاز،
/// وبوابة الشفت (شفت الفرع/المستخدم/الفروع المصرح بها).
/// </summary>
public sealed class ShiftBalancesTests : IClassFixture<TestDb>
{
    private readonly TestDb _db;

    public ShiftBalancesTests(TestDb db) => _db = db;

    private sealed record Fixture(Guid Branch, Guid TreasuryId, Guid CashMethod,
        Partner Customer, Dictionary<string, Account> Chart);

    private async Task<Fixture> SetupAsync(string suffix)
    {
        var actor = await _db.AdminActorAsync();
        var chart = await _db.SeedAccountingAsync();
        var settings = _db.AccountingSettings();
        await settings.SetDefaultAsync(actor, PostingKeys.CustomerAdvances, chart["1110"].Id);
        await settings.SetDefaultAsync(actor, PostingKeys.SupplierAdvances, chart["2100"].Id);
        await settings.SetDefaultAsync(actor, PostingKeys.CashOverShort, chart["5490"].Id);

        var branch = await _db.CreateBranchAsync("SB-B-" + suffix, "فرع شفت " + suffix);
        var treasuryId = Guid.CreateVersion7();
        await _db.TreasuryRepo.CreateTreasuryAsync(treasuryId, new TreasuryInput
        {
            BranchId = branch, Code = "SB-T-" + suffix, NameAr = "خزينة " + suffix,
            TreasuryType = TreasuryTypes.Cash, AccountId = chart["1101"].Id
        }, actor.UserId);

        var cash = Guid.CreateVersion7();
        await _db.TreasuryRepo.CreateMethodAsync(cash, new TreasuryMethodInput
        {
            TreasuryId = treasuryId, Code = "CASH", NameAr = "نقدي",
            MethodType = PaymentMethodTypes.Cash
        }, actor.UserId);

        var customer = await _db.PartnerRepo.CreateAsync(PartnerKind.Customer, new PartnerInput
        { Code = "SB-C-" + suffix, NameAr = "عميل " + suffix });

        return new Fixture(branch, treasuryId, cash, customer, chart);
    }

    private async Task PostReceiptAsync(Fixture f, decimal amount)
    {
        var actor = await _db.AdminActorAsync();
        var service = _db.TreasuryService();
        var draft = await service.CreateVoucherAsync(actor, new VoucherInput
        {
            VoucherType = VoucherTypes.Receipt, BranchId = f.Branch, DocDate = DateTime.Today,
            PartyType = "C", PartyId = f.Customer.Id, PartyName = f.Customer.NameAr,
            TreasuryId = f.TreasuryId,
            Lines = { new VoucherLineInput { TreasuryMethodId = f.CashMethod, Amount = amount } }
        });
        await service.PostVoucherAsync(actor, draft.Id);
    }

    [Fact]
    public async Task Follow_in_shift_flag_controls_opening_rows()
    {
        var actor = await _db.AdminActorAsync();
        var service = _db.TreasuryService();
        var f = await SetupAsync("7");
        await PostReceiptAsync(f, 400);

        var balances = await service.GetOpeningBalancesAsync(actor, f.Branch);
        var row = Assert.Single(balances.Treasuries);
        Assert.Equal(400m, row.PostedBalance);
        Assert.Equal(400m, balances.OpeningCash);

        await service.SetTreasuryFollowInShiftAsync(actor, row.TreasuryId, false);
        var after = await service.GetOpeningBalancesAsync(actor, f.Branch);
        Assert.Empty(after.Treasuries);
        Assert.Equal(0m, after.OpeningCash);

        await service.SetTreasuryFollowInShiftAsync(actor, row.TreasuryId, true);
        var restored = await service.GetOpeningBalancesAsync(actor, f.Branch);
        Assert.Single(restored.Treasuries);
    }

    [Fact]
    public async Task Meters_are_listed_per_device_even_when_zero()
    {
        var actor = await _db.AdminActorAsync();
        var service = _db.TreasuryService();
        var f = await SetupAsync("8");

        await using (var connection = new NpgsqlConnection(TestDb.AdminConnection))
        {
            await connection.OpenAsync();
            await connection.ExecuteAsync("""
                INSERT INTO fixed_assets (id, code, name_ar, category_id, branch_id,
                    purchase_date, cost, is_metered, meter_type, current_meter_value)
                SELECT gen_random_uuid(), @code, @name, (SELECT id FROM asset_categories LIMIT 1),
                       @branch, current_date, 0, true, 'PAGES', @value
                """, new object[]
            {
                new { code = "MTR-0-" + Guid.NewGuid().ToString("N")[..6], name = "جهاز صفر",
                    branch = f.Branch, value = 0m },
                new { code = "MTR-1-" + Guid.NewGuid().ToString("N")[..6], name = "جهاز 120",
                    branch = f.Branch, value = 120m }
            });
        }

        var balances = await service.GetOpeningBalancesAsync(actor, f.Branch);
        Assert.Equal(2, balances.Meters.Count);
        Assert.Contains(balances.Meters, m => m.Value == 0m);
        Assert.Equal(120m, balances.MeterBalance);
    }


    [Fact]
    public async Task Review_posts_variance_to_the_treasury_over_short_account()
    {
        var actor = await _db.AdminActorAsync();
        var service = _db.TreasuryService();
        var f = await SetupAsync("10");

        // حساب زيادة/عجز مخصص للخزينة (بديل الافتراضي 5490) = حساب مصاريف التحصيل 5200.
        await service.SetTreasuryOverShortAccountAsync(actor, f.TreasuryId, f.Chart["5200"].Id);

        var shift = await service.OpenShiftAsync(actor, new ShiftInput { BranchId = f.Branch });
        await PostReceiptAsync(f, 100);
        await service.CloseShiftAsync(actor, shift.Id, new List<ShiftCashCountInput>
        { new() { TreasuryId = f.TreasuryId, CountedCash = 90 } });
        await service.ReviewShiftAsync(actor, shift.Id);

        await using var connection = new NpgsqlConnection(TestDb.AdminConnection);
        await connection.OpenAsync();
        var entryId = await connection.ExecuteScalarAsync<Guid?>(
            "SELECT journal_entry_id FROM shift_cash_counts WHERE shift_id = @shiftId",
            new { shiftId = shift.Id });
        Assert.NotNull(entryId);

        var lines = (await connection.QueryAsync<(string Code, decimal Debit, decimal Credit)>(
            "SELECT a.code, l.debit, l.credit FROM journal_entry_lines l " +
            "JOIN accounts a ON a.id = l.account_id WHERE l.entry_id = @id ORDER BY a.code",
            new { id = entryId })).ToList();
        Assert.Equal(10m, lines.Single(x => x.Code == "5200").Debit);
        Assert.Equal(10m, lines.Single(x => x.Code == "1101").Credit);
    }

    [Fact]
    public async Task Gate_accepts_any_open_shift_of_the_branch_or_of_the_user()
    {
        var admin = await _db.AdminActorAsync();
        var f = await SetupAsync("9");
        await _db.TreasuryRepo.OpenShiftAsync(Guid.CreateVersion7(),
            new ShiftInput { BranchId = f.Branch }, admin.UserId);

        // شفت الفرع تكفي أي مستخدم فيه، وشفت المستخدم نفسه تكفيه في أي فرع.
        Assert.True(await _db.TreasuryRepo.HasOpenShiftAsync(Guid.NewGuid(), f.Branch,
            new[] { f.Branch }));
        Assert.True(await _db.TreasuryRepo.HasOpenShiftAsync(admin.UserId, Guid.NewGuid(),
            Array.Empty<Guid>()));
        // شفت مفتوحة في فرع مصرح به تكفي مستخدم الفروع (البيانات مقيّدة بفروعه).
        Assert.True(await _db.TreasuryRepo.HasOpenShiftAsync(Guid.NewGuid(), Guid.NewGuid(),
            new[] { f.Branch }));
        // لا شفت في أي فرع مصرح به ولا شفت للمستخدم ⇒ لا تسجيل.
        Assert.False(await _db.TreasuryRepo.HasOpenShiftAsync(Guid.NewGuid(), Guid.NewGuid(),
            new[] { Guid.NewGuid() }));
    }
}

