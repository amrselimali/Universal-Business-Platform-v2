using Ubp.Core.Models;
using Ubp.Core.Results;
using Xunit;

namespace Ubp.Tests;

/// <summary>
/// إسناد الموظف المسؤول من إجراءات تعديل الحجز: بلا دور ظاهر — الموظف المختار يصبح
/// المسؤول افتراضياً، مع الحفاظ على توافقية واجهة إسناد الأدوار القديمة.
/// </summary>
public sealed class BookingResponsibleAssignmentTests : IClassFixture<TestDb>
{
    private readonly TestDb _db;

    public BookingResponsibleAssignmentTests(TestDb db) => _db = db;

    private async Task<(Booking Booking, Guid First, Guid Second)> SetupAsync(string suffix)
    {
        var actor = await _db.AdminActorAsync();
        var branch = await _db.CreateBranchAsync("RA-B" + suffix, "فرع الإسناد " + suffix);
        var warehouse = await _db.CreateWarehouseAsync("RA-W" + suffix, "مخزن الإسناد " + suffix,
            branch);
        var item = await _db.CreateItemAsync("RA-IT" + suffix, "صنف الإسناد " + suffix);
        var customer = await _db.PartnerRepo.CreateAsync(PartnerKind.Customer, new PartnerInput
        { Code = "RA-C" + suffix, NameAr = "عميل الإسناد " + suffix });

        var first = Guid.CreateVersion7();
        await _db.EmployeeRepo.CreateAsync(first, new EmployeeInput
        { Code = "RA-E1" + suffix, NameAr = "موظف أول " + suffix, IsServiceExecutor = true },
            actor.UserId);
        var second = Guid.CreateVersion7();
        await _db.EmployeeRepo.CreateAsync(second, new EmployeeInput
        { Code = "RA-E2" + suffix, NameAr = "موظف ثانٍ " + suffix, IsServiceExecutor = true },
            actor.UserId);

        var booking = await _db.BookingService().CreateAsync(actor, new BookingInput
        {
            BranchId = branch, BookingDate = DateTime.Today, CustomerId = customer.Id,
            Lines =
            {
                new BookingLineInput
                {
                    ItemId = item.Id, UnitId = item.BaseUnitId, WarehouseId = warehouse,
                    Quantity = 1, UnitPrice = 10
                }
            }
        });
        return (booking, first, second);
    }

    [Fact]
    public async Task Assigning_responsible_employee_defaults_without_role()
    {
        var actor = await _db.AdminActorAsync();
        var (booking, first, second) = await SetupAsync("1");
        var service = _db.BookingService();

        var assigned = await service.AssignResponsibleEmployeeAsync(actor, booking.Id, first);
        Assert.Equal(first, assigned.AssignedEmployeeId);
        var single = Assert.Single(assigned.Assignments);
        Assert.Equal("owner", single.Role);
        Assert.Null(single.UnassignedAt);

        var reassigned = await service.AssignResponsibleEmployeeAsync(actor, booking.Id, second);
        Assert.Equal(second, reassigned.AssignedEmployeeId);
        Assert.Equal(2, reassigned.Assignments.Count);
        Assert.Single(reassigned.Assignments, a => a.UnassignedAt is null);
        Assert.All(reassigned.Assignments, a => Assert.Equal("owner", a.Role));
    }

    [Fact]
    public async Task Legacy_role_assignment_api_keeps_working()
    {
        var actor = await _db.AdminActorAsync();
        var (booking, first, _) = await SetupAsync("2");
        var service = _db.BookingService();

        var owner = await service.AssignAsync(actor, booking.Id, first, "owner");
        Assert.Equal(first, owner.AssignedEmployeeId);

        var executor = await service.AssignAsync(actor, booking.Id, first, "executor");
        Assert.Equal(first, executor.AssignedEmployeeId);
        Assert.Contains(executor.Assignments,
            a => a.Role == "executor" && a.UnassignedAt is null);
    }

    [Fact]
    public async Task Confirmation_records_the_responsible_employee_from_the_customer_appointment()
    {
        var actor = await _db.AdminActorAsync();
        var bookingBranch = await _db.CreateBranchAsync("RA-B5", "فرع إسناد التأكيد");
        var shiftBranch = await _db.CreateBranchAsync("RA-B6", "فرع دوام التأكيد");
        var warehouse = await _db.CreateWarehouseAsync("RA-W5", "مخزن إسناد التأكيد",
            bookingBranch);
        var unit = await _db.UnitRepo.CreateAsync(new UnitInput
        { Code = "RA-U5", NameAr = "وحدة إسناد التأكيد" });
        var service = await _db.ItemRepo.CreateAsync(new ItemInput
        {
            Code = "RA-SV5", NameAr = "خدمة إسناد التأكيد", ItemType = "SERVICE",
            BaseUnitId = unit.Id,
            Units = new List<ItemUnitInput> { new() { UnitId = unit.Id, ConversionFactor = 1 } }
        });
        var customer = await _db.PartnerRepo.CreateAsync(PartnerKind.Customer, new PartnerInput
        { Code = "RA-C5", NameAr = "عميل إسناد التأكيد" });
        var employeeId = Guid.CreateVersion7();
        await _db.EmployeeRepo.CreateAsync(employeeId, new EmployeeInput
        { Code = "RA-E5", NameAr = "موظف إسناد التأكيد", IsServiceExecutor = true },
            actor.UserId);

        // موعد مؤكد للعميل على فرع آخر يغطي فترة الحجز.
        var start = new DateTimeOffset(DateTime.UtcNow.Date.AddHours(9), TimeSpan.Zero);
        var appointmentId = Guid.CreateVersion7();
        await _db.AppointmentRepo.CreateAsync(appointmentId, new AppointmentInput
        {
            BranchId = shiftBranch, EmployeeId = employeeId, CustomerId = customer.Id,
            StartsAt = start, EndsAt = start.AddHours(4)
        }, actor.UserId);
        var appointment = (await _db.AppointmentRepo.GetAsync(appointmentId))!;
        await _db.AppointmentRepo.SetStatusAsync(appointmentId, appointment.Version,
            AppointmentStatuses.Confirmed, actor.UserId);

        var booking = await _db.BookingService().CreateAsync(actor, new BookingInput
        {
            BranchId = bookingBranch, BookingDate = start.UtcDateTime.Date, CustomerId = customer.Id,
            StartsAt = start.AddHours(1), EndsAt = start.AddMinutes(90),
            Lines =
            {
                new BookingLineInput
                {
                    ItemId = service.Id, UnitId = service.BaseUnitId, WarehouseId = warehouse,
                    Quantity = 1, UnitPrice = 100
                }
            }
        });

        var confirmed = await _db.BookingRepo.ConfirmAsync(booking.Id, actor.UserId,
            reserveStock: false);

        Assert.Equal(employeeId, confirmed.AssignedEmployeeId);
        var open = Assert.Single(confirmed.Assignments, a => a.UnassignedAt is null);
        Assert.Equal("owner", open.Role);
        Assert.Equal(employeeId, open.EmployeeId);
    }

    [Fact]
    public async Task Draft_update_records_the_responsible_employee_by_default()
    {
        var actor = await _db.AdminActorAsync();
        var (booking, first, _) = await SetupAsync("4");

        var input = new BookingInput
        {
            BranchId = booking.BranchId, BookingDate = booking.BookingDate,
            CustomerId = booking.CustomerId, AssignedEmployeeId = first,
            Lines = booking.Lines.Select(l => new BookingLineInput
            {
                ItemId = l.ItemId, UnitId = l.UnitId, WarehouseId = l.WarehouseId,
                Quantity = l.Quantity, UnitPrice = l.UnitPrice
            }).ToList()
        };

        var updated = await _db.BookingService().UpdateAsync(actor, booking.Id,
            booking.Version, input);

        Assert.Equal(first, updated.AssignedEmployeeId);
        var open = Assert.Single(updated.Assignments, a => a.UnassignedAt is null);
        Assert.Equal("owner", open.Role);
        Assert.Equal(first, open.EmployeeId);
    }

    [Fact]
    public async Task Assigning_a_closed_booking_is_rejected()
    {
        var actor = await _db.AdminActorAsync();
        var (booking, first, _) = await SetupAsync("3");
        await _db.BookingService().CancelAsync(actor, booking.Id, "اختبار");

        var error = await Assert.ThrowsAsync<ValidationAppException>(() =>
            _db.BookingService().AssignResponsibleEmployeeAsync(actor, booking.Id, first));
        Assert.Equal("validation.booking_closed", error.Code);
    }
}
