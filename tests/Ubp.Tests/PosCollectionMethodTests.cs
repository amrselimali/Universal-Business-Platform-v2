using Dapper;
using Microsoft.Extensions.Options;
using Npgsql;
using Ubp.Core.Models;
using Ubp.Infrastructure.Repositories;
using Ubp.Web.Services;
using Ubp.Web.Startup.Integrations;
using Xunit;

namespace Ubp.Tests;

/// <summary>
/// طرق التحصيل من شاشة الخزائن: تُقرأ لفرع POS، وتُرحَّل عمولتها إلى حسابها المستقل،
/// ويُرسل إيصال واتساب للعميل عبر صندوق الصادر بلا تكرار.
/// </summary>
public sealed class PosCollectionMethodTests : IClassFixture<TestDb>
{
    private readonly TestDb _db;

    public PosCollectionMethodTests(TestDb db) => _db = db;

    private async Task<(Guid Branch, Guid Warehouse, Partner Customer, Item Service,
        Guid TreasuryId, Guid MethodId)> SetupAsync(string suffix, decimal feePercent,
        Guid? feeAccountId = null)
    {
        var actor = await _db.AdminActorAsync();
        var chart = await _db.SeedAccountingAsync();
        var branch = await _db.CreateBranchAsync("PM-B" + suffix, "فرع طرق التحصيل " + suffix);
        var warehouse = await _db.CreateWarehouseAsync("PM-W" + suffix,
            "مخزن طرق التحصيل " + suffix, branch);
        var unit = await _db.UnitRepo.CreateAsync(new UnitInput
        { Code = "PM-U" + suffix, NameAr = "وحدة طرق التحصيل " + suffix });
        var service = await _db.ItemRepo.CreateAsync(new ItemInput
        {
            Code = "PM-SV" + suffix, NameAr = "خدمة طرق التحصيل " + suffix, ItemType = "SERVICE",
            BaseUnitId = unit.Id,
            Units = new List<ItemUnitInput> { new() { UnitId = unit.Id, ConversionFactor = 1 } }
        });
        var customer = await _db.PartnerRepo.CreateAsync(PartnerKind.Customer, new PartnerInput
        { Code = "PM-C" + suffix, NameAr = "عميل طرق التحصيل " + suffix });

        var treasuryId = Guid.CreateVersion7();
        await _db.TreasuryRepo.CreateTreasuryAsync(treasuryId, new TreasuryInput
        {
            BranchId = branch, Code = "PM-T" + suffix, NameAr = "خزينة طرق التحصيل " + suffix,
            TreasuryType = TreasuryTypes.Cash, AccountId = chart["1101"].Id
        }, actor.UserId);
        var methodId = Guid.CreateVersion7();
        await _db.TreasuryRepo.CreateMethodAsync(methodId, new TreasuryMethodInput
        {
            TreasuryId = treasuryId, Code = "CARD", NameAr = "بطاقة",
            MethodType = PaymentMethodTypes.Card, ClearingAccountId = chart["1130"].Id,
            FeePercent = feePercent, FeeAccountId = feeAccountId
        }, actor.UserId);

        return (branch, warehouse, customer, service, treasuryId, methodId);
    }

    private async Task<PosReceipt> CheckoutAsync(Guid branch, Guid warehouse, Partner customer,
        Item service, Guid treasuryId, Guid methodId, decimal amount) =>
        await _db.Pos().CheckoutAsync(await _db.AdminActorAsync(), new PosCheckoutInput
        {
            BranchId = branch, CustomerId = customer.Id, WarehouseId = warehouse,
            DocDate = DateTime.Today,
            Lines =
            {
                new PosLineInput
                {
                    ItemId = service.Id, UnitId = service.BaseUnitId, Quantity = 1,
                    UnitPrice = amount
                }
            },
            Payments =
            {
                new PosPaymentInput
                {
                    TreasuryId = treasuryId, TreasuryMethodId = methodId, Amount = amount
                }
            }
        });

    [Fact]
    public async Task Pos_fee_posts_to_the_method_fee_account()
    {
        var actor = await _db.AdminActorAsync();
        var chart = await _db.SeedAccountingAsync();
        var f = await SetupAsync("1", 1m, chart["5490"].Id);

        var receipt = await CheckoutAsync(f.Branch, f.Warehouse, f.Customer, f.Service,
            f.TreasuryId, f.MethodId, 100m);
        var voucher = Assert.Single(receipt.Vouchers);

        await using var connection = new NpgsqlConnection(TestDb.AdminConnection);
        await connection.OpenAsync();
        var entries = (await connection.QueryAsync<(string Code, decimal Debit, decimal Credit)>("""
            SELECT a.code, l.debit, l.credit
            FROM journal_entry_lines l
            JOIN accounts a ON a.id = l.account_id
            WHERE l.entry_id = (SELECT journal_entry_id FROM vouchers WHERE id = @id)
            """, new { id = voucher.VoucherId })).ToList();

        Assert.Contains(entries, l => l.Code == chart["5490"].Code && l.Debit == 1m);
        Assert.DoesNotContain(entries, l => l.Code == "5200");

        var detail = await _db.Pos().GetReceiptAsync(actor, voucher.VoucherId);
        Assert.Equal(voucher.VoucherId, detail.Id);
        var line = Assert.Single(detail.Lines);
        Assert.Equal(1m, line.FeeAmount);
        Assert.Equal(100m, line.Amount);
    }

    [Fact]
    public async Task Branch_methods_are_read_from_all_branch_treasuries()
    {
        var actor = await _db.AdminActorAsync();
        var chart = await _db.SeedAccountingAsync();
        var f = await SetupAsync("2", 0m);

        // طريقة ثانية على خزينة أخرى في نفس الفرع.
        var secondTreasury = Guid.CreateVersion7();
        await _db.TreasuryRepo.CreateTreasuryAsync(secondTreasury, new TreasuryInput
        {
            BranchId = f.Branch, Code = "PM-T2B", NameAr = "خزينة ثانية",
            TreasuryType = TreasuryTypes.Cash, AccountId = chart["1101"].Id
        }, actor.UserId);
        var secondMethod = Guid.CreateVersion7();
        await _db.TreasuryRepo.CreateMethodAsync(secondMethod, new TreasuryMethodInput
        { TreasuryId = secondTreasury, Code = "WALLET", NameAr = "محفظة" }, actor.UserId);

        var methods = await _db.TreasuryService().ListBranchMethodsAsync(actor, f.Branch);

        Assert.Contains(methods, m => m.Id == f.MethodId);
        Assert.Contains(methods, m => m.Id == secondMethod);
        Assert.All(methods, m => Assert.NotEqual(Guid.Empty, m.TreasuryId));
    }

    [Fact]
    public async Task Branch_whatsapp_settings_enable_sending_without_global_config()
    {
        var actor = await _db.AdminActorAsync();
        var f = await SetupAsync("7", 0m);
        var settings = await _db.BranchWhatsApp().SaveAsync(actor, new BranchWhatsAppSettingsInput
        {
            BranchId = f.Branch, IsEnabled = true, PhoneNumberId = "111111111111111",
            MessagingAccountId = "222222222222222", AccessToken = "dummy-token"
        });
        Assert.True(settings.IsEnabled);

        await using (var connection = new NpgsqlConnection(TestDb.AdminConnection))
        {
            await connection.OpenAsync();
            await connection.ExecuteAsync(
                "UPDATE customers SET phone = '201001234567' WHERE id = @id",
                new { id = f.Customer.Id });
        }

        var notifier = new WhatsAppReceiptNotifier(new MessageOutboxRepository(_db.Factory),
            _db.PartnerRepo, new BranchWhatsAppSettingsRepository(_db.Factory),
            Options.Create(new WhatsAppOptions { Enabled = false }));
        Assert.True(await notifier.NotifyAsync(f.Branch, f.Customer.Id, "RCV-000123", 100m, 100m,
            null));

        await using var check = new NpgsqlConnection(TestDb.AdminConnection);
        await check.OpenAsync();
        var payload = await check.ExecuteScalarAsync<string>(
            "SELECT payload::text FROM message_outbox WHERE dedupe_key = 'receipt:RCV-000123'");
        Assert.NotNull(payload);
        Assert.Contains(f.Branch.ToString(), payload);
    }

    [Fact]
    public async Task WhatsApp_uses_the_designated_contact_number()
    {
        var f = await SetupAsync("6", 0m);
        await using (var connection = new NpgsqlConnection(TestDb.AdminConnection))
        {
            await connection.OpenAsync();
            await connection.ExecuteAsync("""
                UPDATE customers SET phone = '201000000001', phone2 = '201000000002',
                                     whatsapp_phone = '201000000003'
                WHERE id = @id
                """, new { id = f.Customer.Id });
        }

        var notifier = new WhatsAppReceiptNotifier(new MessageOutboxRepository(_db.Factory),
            _db.PartnerRepo, new BranchWhatsAppSettingsRepository(_db.Factory),
            Options.Create(new WhatsAppOptions { Enabled = true }));
        Assert.True(await notifier.NotifyAsync(f.Branch, f.Customer.Id, "RCV-000099", 100m, 100m, null));

        await using var check = new NpgsqlConnection(TestDb.AdminConnection);
        await check.OpenAsync();
        Assert.Equal("201000000003", await check.ExecuteScalarAsync<string>(
            "SELECT recipient FROM message_outbox WHERE dedupe_key = 'receipt:RCV-000099'"));
    }

    [Fact]
    public async Task WhatsApp_receipt_notification_enqueues_once_with_the_receipt()
    {
        var f = await SetupAsync("4", 0m);
        await using (var connection = new NpgsqlConnection(TestDb.AdminConnection))
        {
            await connection.OpenAsync();
            await connection.ExecuteAsync(
                "UPDATE customers SET phone = '201001234567' WHERE id = @id",
                new { id = f.Customer.Id });
        }

        var notifier = new WhatsAppReceiptNotifier(new MessageOutboxRepository(_db.Factory),
            _db.PartnerRepo, new BranchWhatsAppSettingsRepository(_db.Factory),
            Options.Create(new WhatsAppOptions { Enabled = true }));

        await notifier.NotifyAsync(f.Branch, f.Customer.Id, "RCV-000042", 200m, 100m, 100m);
        await notifier.NotifyAsync(f.Branch, f.Customer.Id, "RCV-000042", 200m, 100m, 100m);

        await using var check = new NpgsqlConnection(TestDb.AdminConnection);
        await check.OpenAsync();
        var rows = (await check.QueryAsync<(string Channel, string Recipient, string Body,
            string Status)>("""
            SELECT channel, recipient, body, status FROM message_outbox
            WHERE dedupe_key = 'receipt:RCV-000042'
            """)).ToList();
        var row = Assert.Single(rows);
        Assert.Equal("WHATSAPP", row.Channel);
        Assert.Equal("201001234567", row.Recipient);
        Assert.Contains("RCV-000042", row.Body);
        Assert.Contains("100", row.Body);
        Assert.Equal("PENDING", row.Status);
    }
}
