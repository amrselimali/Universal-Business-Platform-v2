using Dapper;
using Npgsql;
using Ubp.Core.Models;
using Xunit;

namespace Ubp.Tests;

/// <summary>
/// صنف «تنفيذ مباشر» يُباع من نقطة البيع **مع تحصيل** في نفس العملية: يجب أن يُرحّل الإيراد
/// والمستحق على العميل وصرف المخزون وسند القبض معًا، ويصبح رصيد العميل صفرًا بعد السداد الكامل.
/// </summary>
public sealed class PosDirectPaymentTests : IClassFixture<TestDb>
{
    private readonly TestDb _db;

    public PosDirectPaymentTests(TestDb db) => _db = db;

    [Fact]
    public async Task Pos_direct_item_paid_at_checkout_posts_revenue_and_the_receipt()
    {
        var actor = await _db.AdminActorAsync();
        var chart = await _db.SeedAccountingAsync();
        var branch = await _db.CreateBranchAsync("PDP-B-1", "فرع بيع مباشر مدفوع");
        var warehouse = await _db.CreateWarehouseAsync("PDP-W-1", "مخزن بيع مباشر مدفوع", branch);
        var unit = await _db.UnitRepo.CreateAsync(new UnitInput
        { Code = "PDP-U-1", NameAr = "وحدة بيع مباشر مدفوع" });

        // الصنف يُحفظ من مسار شاشة الأصناف (وليس SQL) كـ«يُنفَّذ مباشرة».
        var item = await _db.ItemService().CreateAsync(actor, new ItemInput
        {
            Code = "PDP-IT-1", NameAr = "صنف بيع مباشر مدفوع", BaseUnitId = unit.Id,
            ExecutionMode = ItemExecutionModes.Direct, DefaultPrice = 100,
            Units = new List<ItemUnitInput> { new() { UnitId = unit.Id, ConversionFactor = 1 } }
        });
        Assert.Equal(ItemExecutionModes.Direct, item.ExecutionMode);
        await _db.PostReceiptAsync(item, branch, warehouse, 10, 40);

        var customer = await _db.PartnerRepo.CreateAsync(PartnerKind.Customer, new PartnerInput
        { Code = "PDP-C-1", NameAr = "عميل بيع مباشر مدفوع" });

        var treasuryId = Guid.CreateVersion7();
        await _db.TreasuryRepo.CreateTreasuryAsync(treasuryId, new TreasuryInput
        {
            BranchId = branch, Code = "PDP-T-1", NameAr = "خزينة بيع مباشر مدفوع",
            TreasuryType = TreasuryTypes.Cash, AccountId = chart["1101"].Id
        }, actor.UserId);
        var cash = Guid.CreateVersion7();
        await _db.TreasuryRepo.CreateMethodAsync(cash, new TreasuryMethodInput
        { TreasuryId = treasuryId, Code = "CASH", NameAr = "نقدي" }, actor.UserId);

        var receipt = await _db.Pos().CheckoutAsync(actor, new PosCheckoutInput
        {
            BranchId = branch, CustomerId = customer.Id, WarehouseId = warehouse,
            DocDate = DateTime.Today,
            Lines =
            {
                new PosLineInput
                { ItemId = item.Id, UnitId = unit.Id, Quantity = 2, UnitPrice = 100 }
            },
            Payments =
            {
                new PosPaymentInput
                { TreasuryId = treasuryId, TreasuryMethodId = cash, Amount = 200 }
            }
        });

        Assert.True(receipt.Executed);
        Assert.Equal(200m, receipt.Paid);
        var voucher = Assert.Single(receipt.Vouchers);
        Assert.Equal(8m, await _db.OnHandAsync(item.Id, warehouse));

        await using var connection = new NpgsqlConnection(TestDb.AdminConnection);
        await connection.OpenAsync();

        // قيد التنفيذ: مستحق على العميل / إيراد (مع التكلفة) في نفس عملية البيع.
        var entries = (await connection.QueryAsync<(string Code, decimal Debit, decimal Credit)>(
            """
            SELECT a.code, l.debit, l.credit FROM journal_entry_lines l
            JOIN accounts a ON a.id = l.account_id
            JOIN journal_entries e ON e.id = l.entry_id
            WHERE e.source_type = 'execution'
              AND e.reference =
                  (SELECT doc_no FROM booking_executions WHERE booking_id = @bookingId)
            """, new { bookingId = receipt.BookingId })).ToList();
        Assert.Contains(entries, l => l.Code == "1110" && l.Debit == 200m);
        Assert.Contains(entries, l => l.Code == "4100" && l.Credit == 200m);
        Assert.Contains(entries, l => l.Code == "5100" && l.Debit == 80m);

        // سند القبض: نقدية مدينة / دفعات العملاء دائنة.
        var voucherEntries = (await connection.QueryAsync<(string Code, decimal Debit, decimal Credit)>(
            """
            SELECT a.code, l.debit, l.credit FROM journal_entry_lines l
            JOIN accounts a ON a.id = l.account_id
            WHERE l.entry_id = (SELECT journal_entry_id FROM vouchers WHERE id = @id)
            """, new { id = voucher.VoucherId })).ToList();
        Assert.Contains(voucherEntries, l => l.Code == "1101" && l.Debit == 200m);
        Assert.Contains(voucherEntries, l => l.Code == "1110" && l.Credit == 200m);

        // بعد السداد الكامل: لا يبقى مستحق على العميل.
        var balance = await connection.ExecuteScalarAsync<decimal>("""
            SELECT COALESCE(SUM(l.debit - l.credit), 0) FROM journal_entry_lines l
            JOIN accounts a ON a.id = l.account_id
            WHERE a.code = '1110' AND l.customer_id = @customerId
            """, new { customerId = customer.Id });
        Assert.Equal(0m, balance);
    }
}
