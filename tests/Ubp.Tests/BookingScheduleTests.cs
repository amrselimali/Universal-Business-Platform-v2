using Ubp.Core.Models;
using Ubp.Core.Paging;
using Ubp.Core.Results;
using Xunit;

namespace Ubp.Tests;

/// <summary>المسودة لا تشترط جدول المواعيد، والتأكيد يشترط موعداً يغطي فترة الحجز.</summary>
public sealed class BookingScheduleTests : IClassFixture<TestDb>
{
    private readonly TestDb _db;

    public BookingScheduleTests(TestDb db) => _db = db;

    private async Task<(Guid Branch, Guid Warehouse, Item Item, Partner Customer, Employee Employee)>
        SetupAsync(string suffix)
    {
        var actor = await _db.AdminActorAsync();
        await _db.SeedAccountingAsync();
        var branch = await _db.CreateBranchAsync("BKS-B-" + suffix, "فرع جدول " + suffix);
        var warehouse = await _db.CreateWarehouseAsync("BKS-W-" + suffix, "مخزن " + suffix, branch);
        var item = await _db.CreateItemAsync("BKS-IT-" + suffix, "صنف " + suffix);
        await _db.PostReceiptAsync(item, branch, warehouse, 10, 50);
        var customer = await _db.PartnerRepo.CreateAsync(PartnerKind.Customer, new PartnerInput
        { Code = "BKS-C-" + suffix, NameAr = "عميل " + suffix });

        var employeeId = Guid.CreateVersion7();
        await _db.EmployeeRepo.CreateAsync(employeeId, new EmployeeInput
        {
            Code = "BKS-E-" + suffix, NameAr = "موظف " + suffix, IsServiceExecutor = true
        }, actor.UserId);
        var employee = (await _db.EmployeeRepo.GetAsync(employeeId))!;
        return (branch, warehouse, item, customer, employee);
    }

    private async Task<Booking> DraftAsync(Guid branch, Guid warehouse, Item item, Partner customer,
        Guid? employeeId, DateTimeOffset start)
    {
        var actor = await _db.AdminActorAsync();
        return await _db.BookingService().CreateAsync(actor, new BookingInput
        {
            BranchId = branch, BookingDate = start.UtcDateTime.Date, CustomerId = customer.Id,
            AssignedEmployeeId = employeeId, StartsAt = start, EndsAt = start.AddMinutes(30),
            Lines = { new BookingLineInput { ItemId = item.Id, UnitId = item.BaseUnitId,
                WarehouseId = warehouse, Quantity = 1, UnitPrice = 100 } }
        });
    }

    [Fact]
    public async Task Draft_needs_no_schedule_but_confirmation_requires_a_covering_appointment()
    {
        var actor = await _db.AdminActorAsync();
        var service = _db.BookingService();
        var (branch, warehouse, item, customer, employee) = await SetupAsync("1");
        var start = DateTimeOffset.UtcNow.Date.AddHours(13);

        // مسودة بلا موظف ولا موعد: مسموحة
        var withoutEmployee = await DraftAsync(branch, warehouse, item, customer, null, start);
        var missingEmployee = await Assert.ThrowsAsync<ValidationAppException>(() =>
            service.ConfirmAsync(actor, withoutEmployee.Id));
        Assert.Equal("validation.booking_employee_required", missingEmployee.Code);

        // مسودة بموظف بلا موعد مطابق: مسموحة
        var withEmployee = await DraftAsync(branch, warehouse, item, customer, employee.Id,
            start.AddHours(1));
        var outside = await Assert.ThrowsAsync<ValidationAppException>(() =>
            service.ConfirmAsync(actor, withEmployee.Id));
        Assert.Equal("validation.booking_outside_employee_schedule", outside.Code);

        // بعد إضافة موعد يغطي الفترة: التأكيد ينجح
        await _db.AppointmentRepo.CreateAsync(Guid.CreateVersion7(), new AppointmentInput
        {
            BranchId = branch, EmployeeId = employee.Id, CustomerId = customer.Id,
            StartsAt = start.AddMinutes(30), EndsAt = start.AddHours(3)
        }, actor.UserId);
        var confirmed = await service.ConfirmAsync(actor, withEmployee.Id);
        Assert.Equal(BookingStatuses.Confirmed, confirmed.Status);
    }
}