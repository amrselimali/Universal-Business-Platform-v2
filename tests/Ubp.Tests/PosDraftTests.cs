using Dapper;
using Npgsql;
using Ubp.Core.Models;
using Xunit;

namespace Ubp.Tests;

/// <summary>ترحيل مسودة من نقطة البيع: إيصالات قبض مسودة بلا أي أثر على الحسابات.</summary>
public sealed class PosDraftTests : IClassFixture<TestDb>
{
    private readonly TestDb _db;

    public PosDraftTests(TestDb db) => _db = db;

    [Fact]
    public async Task Draft_only_checkout_creates_draft_vouchers_without_accounting()
    {
        var actor = await _db.AdminActorAsync();
        var chart = await _db.SeedAccountingAsync();
        await _db.AccountingSettings().SetDefaultAsync(actor, PostingKeys.CustomerAdvances,
            chart["1110"].Id);

        var branch = await _db.CreateBranchAsync("PD-B", "فرع مسودة");
        var warehouse = await _db.CreateWarehouseAsync("PD-W", "مخزن مسودة", branch);
        var item = await _db.CreateItemAsync("PD-IT", "صنف مسودة");
        await _db.PostReceiptAsync(item, branch, warehouse, 10, 50);
        var customer = await _db.PartnerRepo.CreateAsync(PartnerKind.Customer,
            new PartnerInput { Code = "PD-C", NameAr = "عميل مسودة" });

        var treasuryId = Guid.CreateVersion7();
        await _db.TreasuryRepo.CreateTreasuryAsync(treasuryId, new TreasuryInput
        {
            BranchId = branch, Code = "PD-T", NameAr = "خزينة مسودة",
            TreasuryType = TreasuryTypes.Cash, AccountId = chart["1101"].Id
        }, actor.UserId);
        var methodId = Guid.CreateVersion7();
        await _db.TreasuryRepo.CreateMethodAsync(methodId, new TreasuryMethodInput
        { TreasuryId = treasuryId, Code = "CASH", NameAr = "نقدي" }, actor.UserId);

        var receipt = await _db.Pos().CheckoutAsync(actor, new PosCheckoutInput
        {
            BranchId = branch, CustomerId = customer.Id, WarehouseId = warehouse,
            DocDate = DateTime.Today, DraftOnly = true,
            Lines =
            {
                new PosLineInput { ItemId = item.Id, UnitId = item.BaseUnitId, Quantity = 1,
                    UnitPrice = 100 }
            },
            Payments =
            {
                new PosPaymentInput { TreasuryId = treasuryId, TreasuryMethodId = methodId,
                    Amount = 100 }
            }
        });

        var voucher = Assert.Single(receipt.Vouchers);
        Assert.Null(voucher.VoucherNo);

        await using var connection = new NpgsqlConnection(TestDb.AdminConnection);
        await connection.OpenAsync();
        var head = await connection.QuerySingleAsync<(string Status, string? DocNo,
            Guid? JournalEntryId)>(
            "SELECT status, doc_no, journal_entry_id FROM vouchers WHERE id = @id",
            new { id = voucher.VoucherId });
        Assert.Equal("draft", head.Status);
        // رقم الإيصال يُحفظ منذ الحفظ (مسودة) ليظهر في عرض/طباعة الإيصال — 0095.
        Assert.StartsWith("RCV-", head.DocNo);
        Assert.Null(head.JournalEntryId);
        // «تسجيل» (مسودة) يخصّص المبلغ على الحجز ليخفض غير المسدد رغم أنه بلا ترحيل محاسبي — 0095.
        var paid = await connection.ExecuteScalarAsync<decimal>(
            "SELECT COALESCE(SUM(amount), 0) FROM booking_payments WHERE voucher_id = @id",
            new { id = voucher.VoucherId });
        Assert.Equal(100m, paid);
    }
}
