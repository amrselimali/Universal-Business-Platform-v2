using Ubp.Core.Models;
using Ubp.Core.Security;

namespace Ubp.Tests;

internal sealed record TreasuryFixture(Guid Branch, Guid TreasuryId, Guid CashMethod,
    Guid CardMethod, Partner Customer, Dictionary<string, Account> Chart);

/// <summary>بيانات مشتركة لاختبارات الخزينة: خزينة نقدية + نقدي + بطاقة بعمولة 2%.</summary>
internal static class TreasuryFixtures
{
    public static async Task<TreasuryFixture> CreateAsync(TestDb db, string suffix)
    {
        var actor = await db.AdminActorAsync();
        var chart = await db.SeedAccountingAsync();
        await MapDefaultsAsync(db, actor, chart);

        var branch = await db.CreateBranchAsync("TV-B-" + suffix, "فرع خزينة " + suffix);
        var treasuryId = Guid.CreateVersion7();
        await db.TreasuryRepo.CreateTreasuryAsync(treasuryId, new TreasuryInput
        {
            BranchId = branch, Code = "T-" + suffix, NameAr = "خزينة " + suffix,
            TreasuryType = TreasuryTypes.Cash, AccountId = chart["1101"].Id
        }, actor.UserId);

        var cash = Guid.CreateVersion7();
        await db.TreasuryRepo.CreateMethodAsync(cash, new TreasuryMethodInput
        {
            TreasuryId = treasuryId, Code = "CASH", NameAr = "نقدي",
            MethodType = PaymentMethodTypes.Cash
        }, actor.UserId);

        var card = Guid.CreateVersion7();
        await db.TreasuryRepo.CreateMethodAsync(card, new TreasuryMethodInput
        {
            TreasuryId = treasuryId, Code = "CARD", NameAr = "بطاقة",
            MethodType = PaymentMethodTypes.Card,
            ClearingAccountId = chart["1130"].Id, FeePercent = 2m
        }, actor.UserId);

        var customer = await db.PartnerRepo.CreateAsync(PartnerKind.Customer, new PartnerInput
        {
            Code = "TV-C-" + suffix, NameAr = "عميل " + suffix
        });

        return new TreasuryFixture(branch, treasuryId, cash, card, customer, chart);
    }

    public static async Task MapDefaultsAsync(TestDb db, ActorContext actor,
        Dictionary<string, Account> chart)
    {
        var settings = db.AccountingSettings();
        await settings.SetDefaultAsync(actor, PostingKeys.CustomerAdvances, chart["1110"].Id);
        await settings.SetDefaultAsync(actor, PostingKeys.SupplierAdvances, chart["2100"].Id);
        await settings.SetDefaultAsync(actor, PostingKeys.PaymentFees, chart["5200"].Id);
        await settings.SetDefaultAsync(actor, PostingKeys.CashOverShort, chart["5490"].Id);
        await settings.SetDefaultAsync(actor, PostingKeys.InterBranchClearing, chart["1130"].Id);
    }

    public static VoucherInput Receipt(TreasuryFixture f, params VoucherLineInput[] lines) => new()
    {
        VoucherType = VoucherTypes.Receipt, BranchId = f.Branch, DocDate = DateTime.Today,
        PartyType = "C", PartyId = f.Customer.Id, PartyName = f.Customer.NameAr,
        TreasuryId = f.TreasuryId, Lines = lines.ToList()
    };

    /// <summary>فاتورة شراء مرحّلة (صنف واحد، بلا ضريبة) لتخصيص الدفعات عليها.</summary>
    public static async Task<CommercialDocument> PostPurchaseInvoiceAsync(TestDb db,
        TreasuryFixture f, string suffix, decimal amount)
    {
        var actor = await db.AdminActorAsync();
        var category = await db.ItemCategoryRepo.CreateAsync(new ItemCategoryInput
        { Code = "TV-CAT-" + suffix, NameAr = "تصنيف " + suffix });
        var unit = await db.UnitRepo.CreateAsync(new UnitInput
        { Code = "TV-U-" + suffix, NameAr = "وحدة " + suffix });
        var item = await db.ItemRepo.CreateAsync(new ItemInput
        {
            Code = "TV-IT-" + suffix, NameAr = "صنف " + suffix, CategoryId = category.Id,
            BaseUnitId = unit.Id,
            Units = { new ItemUnitInput { UnitId = unit.Id, ConversionFactor = 1 } }
        });
        await db.AccountingSettings().SaveCategoryAccountAsync(actor, new ItemCategoryAccountInput
        {
            CategoryId = category.Id, InventoryAccountId = f.Chart["1120"].Id,
            CogsAccountId = f.Chart["5100"].Id
        });

        var supplier = await db.PartnerRepo.CreateAsync(PartnerKind.Supplier, new PartnerInput
        { Code = "TV-S-" + suffix, NameAr = "مورد " + suffix });
        var warehouse = await db.CreateWarehouseAsync("TV-W-" + suffix, "مخزن " + suffix, f.Branch);

        var id = Guid.CreateVersion7();
        await db.CommercialRepo.CreateDraftAsync(id, new CommercialDocumentInput
        {
            DocType = CommercialDocTypes.PurchaseInvoice, BranchId = f.Branch,
            WarehouseId = warehouse, DocDate = DateTime.Today, SupplierId = supplier.Id,
            Lines =
            {
                new CommercialDocumentLineInput
                {
                    ItemId = item.Id, UnitId = item.BaseUnitId, ConversionFactor = 1,
                    Quantity = 1, UnitPrice = amount
                }
            }
        }, actor.UserId);
        return await db.CommercialRepo.PostAsync(id, actor.UserId, false);
    }

    public static async Task<Voucher> PostPaymentAsync(TestDb db, TreasuryFixture f,
        Guid supplierId, string supplierName, decimal amount)
    {
        var actor = await db.AdminActorAsync();
        var service = db.TreasuryService();
        var draft = await service.CreateVoucherAsync(actor, new VoucherInput
        {
            VoucherType = VoucherTypes.Payment, BranchId = f.Branch, DocDate = DateTime.Today,
            PartyType = "S", PartyId = supplierId, PartyName = supplierName,
            TreasuryId = f.TreasuryId,
            Lines = { new VoucherLineInput { TreasuryMethodId = f.CashMethod, Amount = amount } }
        });
        return await service.PostVoucherAsync(actor, draft.Id);
    }
}
