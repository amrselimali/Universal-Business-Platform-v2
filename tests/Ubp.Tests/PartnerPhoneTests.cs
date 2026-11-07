using Ubp.Core.Models;
using Ubp.Core.Results;
using Xunit;

namespace Ubp.Tests;

/// <summary>رقم هاتف العميل لا يُقبل مكررًا عند التكويد/التعديل.</summary>
public sealed class PartnerPhoneTests : IClassFixture<TestDb>
{
    private readonly TestDb _db;

    public PartnerPhoneTests(TestDb db) => _db = db;

    [Fact]
    public async Task Duplicate_customer_phone_is_rejected_on_create_and_update()
    {
        var actor = await _db.AdminActorAsync();
        var service = _db.Partners();

        var first = await service.CreateAsync(actor, PartnerKind.Customer, new PartnerInput
        { Code = "PH-C-1", NameAr = "عميل هاتف 1", Phone = "01000000001" });
        Assert.Equal("01000000001", first.Phone);

        var dup = await Assert.ThrowsAsync<ValidationAppException>(() =>
            service.CreateAsync(actor, PartnerKind.Customer, new PartnerInput
            { Code = "PH-C-2", NameAr = "عميل هاتف 2", Phone = "01000000001" }));
        Assert.Equal("validation.customer_phone_duplicate", dup.Code);

        // رقم مختلف مسموح.
        var second = await service.CreateAsync(actor, PartnerKind.Customer, new PartnerInput
        { Code = "PH-C-3", NameAr = "عميل هاتف 3", Phone = "01000000002" });

        // تعديل نفس العميل مع الإبقاء على رقمه مسموح (استثناء الذات).
        var updatedSelf = await service.UpdateAsync(actor, PartnerKind.Customer, first.Id,
            first.Version, new PartnerInput
            { Code = first.Code, NameAr = "عميل هاتف 1 معدل", Phone = "01000000001" });
        Assert.Equal("01000000001", updatedSelf.Phone);

        // تعديل عميل لرقم عميل آخر مرفوض.
        var dupUpdate = await Assert.ThrowsAsync<ValidationAppException>(() =>
            service.UpdateAsync(actor, PartnerKind.Customer, second.Id, second.Version,
                new PartnerInput
                { Code = second.Code, NameAr = second.NameAr, Phone = "01000000001" }));
        Assert.Equal("validation.customer_phone_duplicate", dupUpdate.Code);
    }
}