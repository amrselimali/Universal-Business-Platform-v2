using Dapper;
using Npgsql;
using Ubp.Core.Models;
using Xunit;

namespace Ubp.Tests;

/// <summary>
/// الشفت يحتسب إيراد التنفيذ المباشر من نقطة البيع (بند 7)، وشاشة إيصالات POS
/// تعرض الخزينة والخدمة مع فلتر يوم/يوم وإجمالي — 0094.
/// </summary>
public sealed class ShiftRevenueTests : IClassFixture<TestDb>
{
    private readonly TestDb _db;

    public ShiftRevenueTests(TestDb db) => _db = db;

    private sealed record Fixture(Guid Branch, Guid Warehouse, Item Item, Partner Customer,
        Guid TreasuryId, Guid CashMethod);

    private async Task<Fixture> SetupAsync(string suffix)
    {
        var actor = await _db.AdminActorAsync();
        var chart = await _db.SeedAccountingAsync();
        var settings = _db.AccountingSettings();
        await settings.SetDefaultAsync(actor, PostingKeys.CustomerAdvances, chart["1110"].Id);

        var branch = await _db.CreateBranchAsync("SR-B-" + suffix, "فرع إيراد الشفت " + suffix);
        var warehouse = await _db.CreateWarehouseAsync("SR-W-" + suffix,
            "مخزن إيراد الشفت " + suffix, branch);
        var item = await _db.CreateItemAsync("SR-IT-" + suffix, "صنف إيراد الشفت " + suffix);
        await _db.PostReceiptAsync(item, branch, warehouse, 20, 40);

        var treasuryId = Guid.CreateVersion7();
        await _db.TreasuryRepo.CreateTreasuryAsync(treasuryId, new TreasuryInput
        {
            BranchId = branch, Code = "SR-T-" + suffix, NameAr = "خزينة إيراد " + suffix,
            TreasuryType = TreasuryTypes.Cash, AccountId = chart["1101"].Id
        }, actor.UserId);
        var cash = Guid.CreateVersion7();
        await _db.TreasuryRepo.CreateMethodAsync(cash, new TreasuryMethodInput
        {
            TreasuryId = treasuryId, Code = "CASH", NameAr = "نقدي",
            MethodType = PaymentMethodTypes.Cash
        }, actor.UserId);

        var customer = await _db.PartnerRepo.CreateAsync(PartnerKind.Customer, new PartnerInput
        { Code = "SR-C-" + suffix, NameAr = "عميل إيراد " + suffix });

        // الصنف يُنفَّذ مباشرة من نقطة البيع.
        await using var connection = new NpgsqlConnection(TestDb.AdminConnection);
        await connection.OpenAsync();
        await connection.ExecuteAsync(
            "UPDATE items SET execution_mode = @mode WHERE id = @id",
            new { mode = ItemExecutionModes.Direct, id = item.Id });

        return new Fixture(branch, warehouse, item, customer, treasuryId, cash);
    }

    private async Task<PosReceipt> CheckoutAsync(Fixture f, decimal quantity, decimal paid)
    {
        var actor = await _db.AdminActorAsync();
        var input = new PosCheckoutInput
        {
            BranchId = f.Branch, CustomerId = f.Customer.Id, WarehouseId = f.Warehouse,
            DocDate = DateTime.Today,
            Lines =
            {
                new PosLineInput
                {
                    ItemId = f.Item.Id, UnitId = f.Item.BaseUnitId,
                    Quantity = quantity, UnitPrice = 100
                }
            }
        };
        if (paid > 0)
            input.Payments.Add(new PosPaymentInput { TreasuryId = f.TreasuryId, Amount = paid });
        return await _db.Pos().CheckoutAsync(actor, input);
    }

    [Fact]
    public async Task Pos_direct_execution_counts_as_revenue_in_the_shift_report()
    {
        var actor = await _db.AdminActorAsync();
        var f = await SetupAsync("1");
        var shifts = _db.TreasuryService();
        var shift = await shifts.OpenShiftAsync(actor, new ShiftInput { BranchId = f.Branch });

        var receipt = await CheckoutAsync(f, 4, paid: 0);
        Assert.True(receipt.Executed);

        var report = await shifts.GetShiftReportAsync(actor, shift.Id);
        var row = Assert.Single(report.Lines.Where(l => l.Quantity > 0));
        Assert.Equal(400m, row.Revenue);
        Assert.Equal(400m, report.RevenuesTotal);
    }

    [Fact]
    public async Task Pos_receipts_expose_treasury_and_services_with_day_filter()
    {
        var actor = await _db.AdminActorAsync();
        var f = await SetupAsync("2");
        await _db.TreasuryService().OpenShiftAsync(actor, new ShiftInput { BranchId = f.Branch });
        var receipt = await CheckoutAsync(f, 2, paid: 200);
        Assert.Single(receipt.Vouchers);

        var rows = await _db.Pos().ListReceiptsAsync(actor, f.Branch, DateTime.Today,
            DateTime.Today, 100);
        var row = Assert.Single(rows);
        Assert.Equal("خزينة إيراد 2", row.TreasuryName);
        Assert.Contains(f.Item.NameAr, row.Services);
        Assert.Equal(200m, row.Total);

        // فلتر يوم لا يشمله => لا نتائج.
        var outside = await _db.Pos().ListReceiptsAsync(actor, f.Branch,
            DateTime.Today.AddDays(-5), DateTime.Today.AddDays(-4), 100);
        Assert.Empty(outside);
    }
}
