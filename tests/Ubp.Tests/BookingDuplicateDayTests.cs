using Ubp.Core.Models;
using Ubp.Core.Results;
using Xunit;

namespace Ubp.Tests;

/// <summary>لا يُقبل أكثر من حجز/موعد لنفس العميل مع نفس الموظف المسؤول في نفس اليوم.</summary>
public sealed class BookingDuplicateDayTests : IClassFixture<TestDb>
{
    private readonly TestDb _db;

    public BookingDuplicateDayTests(TestDb db) => _db = db;

    private sealed record Fx(Guid Branch, Guid Warehouse, Item Service, Partner Customer,
        Guid Employee1, Guid Employee2);

    private async Task<Fx> SetupAsync()
    {
        var actor = await _db.AdminActorAsync();
        var branch = await _db.CreateBranchAsync("DD-B-1", "فرع تكرار الموعد");
        var warehouse = await _db.CreateWarehouseAsync("DD-W-1", "مخزن تكرار الموعد", branch);
        var unit = await _db.UnitRepo.CreateAsync(new UnitInput { Code = "DD-U-1", NameAr = "وحدة" });
        var service = await _db.ItemRepo.CreateAsync(new ItemInput
        {
            Code = "DD-SV-1", NameAr = "خدمة تكرار", ItemType = "SERVICE", BaseUnitId = unit.Id,
            Units = new List<ItemUnitInput> { new() { UnitId = unit.Id, ConversionFactor = 1 } }
        });
        var customer = await _db.PartnerRepo.CreateAsync(PartnerKind.Customer,
            new PartnerInput { Code = "DD-C-1", NameAr = "عميل تكرار" });
        var e1 = Guid.CreateVersion7();
        await _db.EmployeeRepo.CreateAsync(e1, new EmployeeInput
        { Code = "DD-E-1", NameAr = "موظف 1", IsServiceExecutor = true }, actor.UserId);
        var e2 = Guid.CreateVersion7();
        await _db.EmployeeRepo.CreateAsync(e2, new EmployeeInput
        { Code = "DD-E-2", NameAr = "موظف 2", IsServiceExecutor = true }, actor.UserId);
        return new Fx(branch, warehouse, service, customer, e1, e2);
    }

    private BookingInput Input(Fx f, Guid employee, DateTime date) => new()
    {
        BranchId = f.Branch, BookingDate = date, CustomerId = f.Customer.Id,
        AssignedEmployeeId = employee,
        Lines = { new BookingLineInput { ItemId = f.Service.Id, UnitId = f.Service.BaseUnitId,
            WarehouseId = f.Warehouse, Quantity = 1, UnitPrice = 0 } }
    };

    [Fact]
    public async Task Second_booking_same_customer_employee_day_is_rejected()
    {
        var actor = await _db.AdminActorAsync();
        var svc = _db.BookingService();
        var f = await SetupAsync();
        var today = DateTime.Today;

        var first = await svc.CreateAsync(actor, Input(f, f.Employee1, today));
        Assert.NotEqual(Guid.Empty, first.Id);

        var dup = await Assert.ThrowsAsync<ValidationAppException>(() =>
            svc.CreateAsync(actor, Input(f, f.Employee1, today)));
        Assert.Equal("validation.booking_duplicate_same_day", dup.Code);

        // نفس العميل + يوم مختلف => مسموح.
        await svc.CreateAsync(actor, Input(f, f.Employee1, today.AddDays(1)));
        // نفس العميل + نفس اليوم + موظف آخر => مسموح.
        await svc.CreateAsync(actor, Input(f, f.Employee2, today));
        // تعديل الحجز الأول مع الإبقاء على نفس الموظف/اليوم => مسموح (استثناء الذات).
        await svc.UpdateAsync(actor, first.Id, first.Version, Input(f, f.Employee1, today));
    }
}