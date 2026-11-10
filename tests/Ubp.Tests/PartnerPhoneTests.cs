using Ubp.Core.Models;
using Ubp.Core.Results;
using Ubp.Core.Security;
using Xunit;

namespace Ubp.Tests;

/// <summary>رقم هاتف العميل لا يُقبل مكررًا عند التكويد/التعديل، والتصنيف إجباري.</summary>
public sealed class PartnerPhoneTests : IClassFixture<TestDb>
{
    private readonly TestDb _db;

    public PartnerPhoneTests(TestDb db) => _db = db;

    private async Task<Guid> CategoryAsync(string code) =>
        (await _db.PartnerRepo.CreateCategoryAsync(PartnerKind.Customer,
            new PartnerCategoryInput { Code = code, NameAr = "تصنيف " + code })).Id;

    [Fact]
    public async Task Duplicate_customer_phone_is_rejected_on_create_and_update()
    {
        var actor = await _db.AdminActorAsync();
        var service = _db.Partners();
        var categoryId = await CategoryAsync("PH-CAT-1");

        var first = await service.CreateAsync(actor, PartnerKind.Customer, new PartnerInput
        { Code = "PH-C-1", NameAr = "عميل هاتف 1", CategoryId = categoryId,
            Phone = "01000000001", WhatsAppPhone = "01000000001" });
        Assert.Equal("01000000001", first.Phone);

        var dup = await Assert.ThrowsAsync<ValidationAppException>(() =>
            service.CreateAsync(actor, PartnerKind.Customer, new PartnerInput
            { Code = "PH-C-2", NameAr = "عميل هاتف 2", CategoryId = categoryId,
                Phone = "01000000001", WhatsAppPhone = "01000000001" }));
        Assert.Equal("validation.customer_phone_duplicate", dup.Code);

        // رقم مختلف مسموح.
        var second = await service.CreateAsync(actor, PartnerKind.Customer, new PartnerInput
        { Code = "PH-C-3", NameAr = "عميل هاتف 3", CategoryId = categoryId,
            Phone = "01000000002", WhatsAppPhone = "01000000002" });

        // تعديل نفس العميل مع الإبقاء على رقمه مسموح (استثناء الذات).
        var updatedSelf = await service.UpdateAsync(actor, PartnerKind.Customer, first.Id,
            first.Version, new PartnerInput
            { Code = first.Code, NameAr = "عميل هاتف 1 معدل", CategoryId = categoryId,
                Phone = "01000000001", WhatsAppPhone = "01000000001" });
        Assert.Equal("01000000001", updatedSelf.Phone);

        // تعديل عميل لرقم عميل آخر مرفوض.
        var dupUpdate = await Assert.ThrowsAsync<ValidationAppException>(() =>
            service.UpdateAsync(actor, PartnerKind.Customer, second.Id, second.Version,
                new PartnerInput
                { Code = second.Code, NameAr = second.NameAr, CategoryId = categoryId,
                    Phone = "01000000001", WhatsAppPhone = "01000000001" }));
        Assert.Equal("validation.customer_phone_duplicate", dupUpdate.Code);
    }

    [Fact]
    public async Task Customer_requires_phone_and_whatsapp()
    {
        var actor = await _db.AdminActorAsync();
        var service = _db.Partners();
        var categoryId = await CategoryAsync("PH-CAT-2");

        var noPhone = await Assert.ThrowsAsync<ValidationAppException>(() =>
            service.CreateAsync(actor, PartnerKind.Customer, new PartnerInput
            { Code = "PH-R-1", NameAr = "عميل بلا هاتف", CategoryId = categoryId }));
        Assert.Equal("validation.customer_phone_required", noPhone.Code);

        var noWhatsApp = await Assert.ThrowsAsync<ValidationAppException>(() =>
            service.CreateAsync(actor, PartnerKind.Customer, new PartnerInput
            { Code = "PH-R-2", NameAr = "عميل بلا واتساب", CategoryId = categoryId,
                Phone = "01000000009" }));
        Assert.Equal("validation.customer_whatsapp_required", noWhatsApp.Code);
    }

    [Fact]
    public async Task Customer_requires_a_category_and_gets_a_branch_serial_code()
    {
        var actor = await _db.AdminActorAsync();
        var service = _db.Partners();
        var branch = await _db.CreateBranchAsync("PH-BR", "فرع تكويد العملاء");
        var categoryId = await CategoryAsync("PH-CAT-3");

        // التصنيف إجباري للعميل.
        var noCategory = await Assert.ThrowsAsync<ValidationAppException>(() =>
            service.CreateAsync(actor, PartnerKind.Customer, new PartnerInput
            {
                NameAr = "عميل بلا تصنيف", ScopeType = "BRANCH", BranchId = branch,
                Phone = "01055555551", WhatsAppPhone = "01055555551"
            }));
        Assert.Equal("validation.partner_category_required", noCategory.Code);

        // الكود يُولَّد آليًا داخل الفرع ويتجاهل أي كود مُرسل.
        var first = await service.CreateAsync(actor, PartnerKind.Customer, new PartnerInput
        {
            Code = "IGNORED-1", NameAr = "عميل تسلسل 1", CategoryId = categoryId,
            ScopeType = "BRANCH", BranchId = branch,
            Phone = "01055555552", WhatsAppPhone = "01055555552"
        });
        var second = await service.CreateAsync(actor, PartnerKind.Customer, new PartnerInput
        {
            Code = "IGNORED-2", NameAr = "عميل تسلسل 2", CategoryId = categoryId,
            ScopeType = "BRANCH", BranchId = branch,
            Phone = "01055555553", WhatsAppPhone = "01055555553"
        });
        // أول عميل في الفرع يبدأ من 1 ثم يتزايد بمقدار 1.
        Assert.Equal("1", first.Code);
        Assert.Equal("2", second.Code);
    }

    [Fact]
    public async Task Restricted_user_cannot_attach_a_partner_to_a_foreign_branch_or_company()
    {
        var admin = await _db.AdminActorAsync();
        var allowed = await _db.CreateBranchAsync("PB-A", "فرع نطاق أ");
        var foreign = await _db.CreateBranchAsync("PB-B", "فرع نطاق ب");
        var userId = await _db.CreateUserAsync("pb-user", "Passw0rd!", new[] { allowed });
        var limited = new ActorContext
        {
            UserId = userId, Username = "pb-user", IsAdmin = true, Language = "ar",
            AllowedBranchIds = new[] { allowed }, AllBranchIds = new[] { allowed },
            CurrentBranchId = allowed, TraceId = "t"
        };
        var svc = _db.Partners();
        var categoryId = await CategoryAsync("PB-CAT");

        // فرع أجنبي مرفوض.
        await Assert.ThrowsAsync<ForbiddenException>(() => svc.CreateAsync(limited,
            PartnerKind.Customer, new PartnerInput
            {
                NameAr = "عميل فرع أجنبي", ScopeType = "BRANCH", CategoryId = categoryId,
                BranchId = foreign, Phone = "01011111111", WhatsAppPhone = "01011111111"
            }));

        // النطاق «الشركة» مرفوض لغير السوبر أدمن.
        await Assert.ThrowsAsync<ForbiddenException>(() => svc.CreateAsync(limited,
            PartnerKind.Customer, new PartnerInput
            {
                NameAr = "عميل شركة", ScopeType = "COMPANY", CategoryId = categoryId,
                Phone = "01022222222", WhatsAppPhone = "01022222222"
            }));

        // فرعه المسموح مقبول.
        var ok = await svc.CreateAsync(limited, PartnerKind.Customer, new PartnerInput
        {
            NameAr = "عميل فرعه", ScopeType = "BRANCH", BranchId = allowed,
            CategoryId = categoryId, Phone = "01033333333", WhatsAppPhone = "01033333333"
        });
        Assert.Equal(allowed, ok.BranchId);
        _ = admin;
    }
}