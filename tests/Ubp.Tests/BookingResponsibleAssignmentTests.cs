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
