using Dapper;
using Npgsql;
using Ubp.Core.Models;
using Ubp.Core.Results;
using Xunit;

namespace Ubp.Tests;

/// <summary>
/// ربط تنفيذ الجلسة بالموظف المسؤول المسجل على الحجز (بغض النظر عن تغيّر أدوار الموظف
/// أو الدوام لاحقًا)، مع الإبقاء على مسار موعد العميل المؤكد كتوافق خلفي.
/// </summary>
public sealed class BookingExecutionEmployeeTests : IClassFixture<TestDb>
{
    private readonly TestDb _db;

    public BookingExecutionEmployeeTests(TestDb db) => _db = db;

    [Fact]
    public async Task Legacy_execution_employee_falls_back_to_a_customer_appointment()
    {
        var actor = await _db.AdminActorAsync();
        await _db.SeedAccountingAsync();
        var branch = await _db.CreateBranchAsync("EX-B-2", "فرع تنفيذ مؤكد");
        var warehouse = await _db.CreateWarehouseAsync("EX-W-2", "مخزن تنفيذ مؤكد", branch);
        var unit = await _db.UnitRepo.CreateAsync(new UnitInput
        {
            Code = "EX-U-2", NameAr = "وحدة تنفيذ مؤكد"
        });
        var service = await _db.ItemRepo.CreateAsync(new ItemInput
        {
            Code = "EX-SV-2", NameAr = "خدمة تنفيذ مؤكد", ItemType = "SERVICE",
            BaseUnitId = unit.Id,
            Units = new List<ItemUnitInput> { new() { UnitId = unit.Id, ConversionFactor = 1 } }
        });
        var customer = await _db.PartnerRepo.CreateAsync(PartnerKind.Customer, new PartnerInput
        { Code = "EX-C-2", NameAr = "عميل تنفيذ مؤكد" });

        var employeeId = Guid.CreateVersion7();
        await _db.EmployeeRepo.CreateAsync(employeeId, new EmployeeInput
        {
            Code = "EX-E-2", NameAr = "موظف تنفيذ مؤكد", IsServiceExecutor = true
        }, actor.UserId);

        // حجز قديم بلا موظف مسؤول على الحجز: يبقى مسار موعد العميل المؤكد كتوافق خلفي.
        var booking = await _db.BookingService().CreateAsync(actor, new BookingInput
        {
            BranchId = branch, BookingDate = DateTime.Today, CustomerId = customer.Id,
            Lines =
            {
                new BookingLineInput
                {
                    ItemId = service.Id, UnitId = service.BaseUnitId, WarehouseId = warehouse,
                    Quantity = 1, UnitPrice = 100
                }
            }
        });
        await _db.BookingRepo.ConfirmAsync(booking.Id, actor.UserId, reserveStock: false);
        var line = (await _db.BookingService().GetAsync(actor, booking.Id)).Lines.Single();

        var start = new DateTimeOffset(DateTime.UtcNow.Date.AddHours(11), TimeSpan.Zero);
        var appointmentId = Guid.CreateVersion7();
        await _db.AppointmentRepo.CreateAsync(appointmentId, new AppointmentInput
        {
            BranchId = branch, EmployeeId = employeeId, CustomerId = customer.Id,
            StartsAt = start, EndsAt = start.AddMinutes(30)
        }, actor.UserId);

        // موعد العميل (غير الملغى) يُعتمد كتوافق خلفي، والمؤكد يُفضَّل عند التعدد.
        var pending = await _db.Executions().GetPreviewAsync(actor, line.Id);
        Assert.Equal("موظف تنفيذ مؤكد", pending.EmployeeName);

        // بعد تأكيد موعد العميل يصبح الموظف المسؤول متاحاً للتنفيذ.
        var appointment = (await _db.AppointmentRepo.GetAsync(appointmentId))!;
        await _db.AppointmentRepo.SetStatusAsync(appointmentId, appointment.Version,
            AppointmentStatuses.Confirmed, actor.UserId);

        var confirmed = await _db.Executions().GetPreviewAsync(actor, line.Id);
        Assert.Equal("موظف تنفيذ مؤكد", confirmed.EmployeeName);
        var result = await _db.Executions().ExecuteAsync(actor, new BookingExecutionInput
        { BookingLineId = line.Id, Quantity = 1 });
        Assert.Equal(1m, result.Quantity);
    }

    [Fact]
    public async Task Execution_resolves_the_responsible_employee_from_a_covering_shift()
    {
        var actor = await _db.AdminActorAsync();
        var branch = await _db.CreateBranchAsync("EX-B-6", "فرع ربط الدوام");
        var warehouse = await _db.CreateWarehouseAsync("EX-W-6", "مخزن ربط الدوام", branch);
        var unit = await _db.UnitRepo.CreateAsync(new UnitInput
        { Code = "EX-U-6", NameAr = "وحدة ربط الدوام" });
        var service = await _db.ItemRepo.CreateAsync(new ItemInput
        {
            Code = "EX-SV-6", NameAr = "خدمة ربط الدوام", ItemType = "SERVICE",
            BaseUnitId = unit.Id,
            Units = new List<ItemUnitInput> { new() { UnitId = unit.Id, ConversionFactor = 1 } }
        });
        var customer = await _db.PartnerRepo.CreateAsync(PartnerKind.Customer, new PartnerInput
        { Code = "EX-C-6", NameAr = "عميل ربط الدوام" });
        var employeeId = Guid.CreateVersion7();
        await _db.EmployeeRepo.CreateAsync(employeeId, new EmployeeInput
        { Code = "EX-E-6", NameAr = "موظف ربط الدوام", IsServiceExecutor = true }, actor.UserId);

        // دوام بلا عميل (كما في شاشة مواعيد الموظفين) يغطي فترة الجلسة.
        var start = new DateTimeOffset(DateTime.UtcNow.Date.AddHours(9), TimeSpan.Zero);
        await _db.AppointmentRepo.CreateAsync(Guid.CreateVersion7(), new AppointmentInput
        {
            BranchId = branch, EmployeeId = employeeId,
            StartsAt = start.AddHours(-1), EndsAt = start.AddHours(2)
        }, actor.UserId);

        var booking = await _db.BookingService().CreateAsync(actor, new BookingInput
        {
            BranchId = branch, BookingDate = start.UtcDateTime.Date, CustomerId = customer.Id,
            AssignedEmployeeId = employeeId, StartsAt = start, EndsAt = start.AddMinutes(30),
            Lines =
            {
                new BookingLineInput
                {
                    ItemId = service.Id, UnitId = service.BaseUnitId, WarehouseId = warehouse,
                    Quantity = 1, UnitPrice = 120
                }
            }
        });
        await _db.BookingRepo.ConfirmAsync(booking.Id, actor.UserId, reserveStock: false);
        var line = (await _db.BookingService().GetAsync(actor, booking.Id)).Lines.Single();

        var preview = await _db.Executions().GetPreviewAsync(actor, line.Id);
        Assert.Equal("موظف ربط الدوام", preview.EmployeeName);

        var result = await _db.Executions().ExecuteAsync(actor, new BookingExecutionInput
        { BookingLineId = line.Id, Quantity = 1 });
        Assert.Equal(1m, result.Quantity);
    }

    [Fact]
    public async Task Execution_resolves_the_customer_appointment_across_branches()
    {
        var actor = await _db.AdminActorAsync();
        var bookingBranch = await _db.CreateBranchAsync("EX-B-8", "فرع الحجز");
        var shiftBranch = await _db.CreateBranchAsync("EX-B-9", "فرع الدوام");
        var warehouse = await _db.CreateWarehouseAsync("EX-W-8", "مخزن ربط الفروع", bookingBranch);
        var unit = await _db.UnitRepo.CreateAsync(new UnitInput
        { Code = "EX-U-8", NameAr = "وحدة ربط الفروع" });
        var service = await _db.ItemRepo.CreateAsync(new ItemInput
        {
            Code = "EX-SV-8", NameAr = "خدمة ربط الفروع", ItemType = "SERVICE",
            BaseUnitId = unit.Id,
            Units = new List<ItemUnitInput> { new() { UnitId = unit.Id, ConversionFactor = 1 } }
        });
        var customer = await _db.PartnerRepo.CreateAsync(PartnerKind.Customer, new PartnerInput
        { Code = "EX-C-8", NameAr = "عميل ربط الفروع" });
        var employeeId = Guid.CreateVersion7();
        await _db.EmployeeRepo.CreateAsync(employeeId, new EmployeeInput
        { Code = "EX-E-8", NameAr = "موظف ربط الفروع", IsServiceExecutor = true }, actor.UserId);

        // حجز مؤكد قديم بلا موظف مسؤول على الحجز (بيانات ما قبل الدوامات).
        var booking = await _db.BookingService().CreateAsync(actor, new BookingInput
        {
            BranchId = bookingBranch, BookingDate = DateTime.Today, CustomerId = customer.Id,
            Lines =
            {
                new BookingLineInput
                {
                    ItemId = service.Id, UnitId = service.BaseUnitId, WarehouseId = warehouse,
                    Quantity = 1, UnitPrice = 100
                }
            }
        });
        await _db.BookingRepo.ConfirmAsync(booking.Id, actor.UserId, reserveStock: false);
        var line = (await _db.BookingService().GetAsync(actor, booking.Id)).Lines.Single();

        // موعد مؤكد للعميل مسجل على فرع آخر ونفس اليوم.
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

        var preview = await _db.Executions().GetPreviewAsync(actor, line.Id);
        Assert.Equal("موظف ربط الفروع", preview.EmployeeName);
    }

    [Fact]
    public async Task Execution_uses_the_responsible_employee_after_the_executor_role_changes()
    {
        var actor = await _db.AdminActorAsync();
        var branch = await _db.CreateBranchAsync("EX-B-7", "فرع تغيّر الدور");
        var warehouse = await _db.CreateWarehouseAsync("EX-W-7", "مخزن تغيّر الدور", branch);
        var unit = await _db.UnitRepo.CreateAsync(new UnitInput
        { Code = "EX-U-7", NameAr = "وحدة تغيّر الدور" });
        var service = await _db.ItemRepo.CreateAsync(new ItemInput
        {
            Code = "EX-SV-7", NameAr = "خدمة تغيّر الدور", ItemType = "SERVICE",
            BaseUnitId = unit.Id,
            Units = new List<ItemUnitInput> { new() { UnitId = unit.Id, ConversionFactor = 1 } }
        });
        var customer = await _db.PartnerRepo.CreateAsync(PartnerKind.Customer, new PartnerInput
        { Code = "EX-C-7", NameAr = "عميل تغيّر الدور" });
        var employeeId = Guid.CreateVersion7();
        await _db.EmployeeRepo.CreateAsync(employeeId, new EmployeeInput
        { Code = "EX-E-7", NameAr = "موظف تغيّر الدور", IsServiceExecutor = true }, actor.UserId);

        var booking = await _db.BookingService().CreateAsync(actor, new BookingInput
        {
            BranchId = branch, BookingDate = DateTime.Today, CustomerId = customer.Id,
            AssignedEmployeeId = employeeId,
            Lines =
            {
                new BookingLineInput
                {
                    ItemId = service.Id, UnitId = service.BaseUnitId, WarehouseId = warehouse,
                    Quantity = 1, UnitPrice = 0
                }
            }
        });
        await _db.BookingRepo.ConfirmAsync(booking.Id, actor.UserId, reserveStock: false);
        var line = (await _db.BookingService().GetAsync(actor, booking.Id)).Lines.Single();

        // تغيّر لاحق في دور الموظف (لم يعد منفذ خدمة) يجب ألا يُعطّل حجزًا مؤكدًا.
        await using (var connection = new NpgsqlConnection(TestDb.AdminConnection))
        {
            await connection.OpenAsync();
            await connection.ExecuteAsync(
                "UPDATE employees SET is_service_executor = false WHERE id = @id",
                new { id = employeeId });
        }

        var preview = await _db.Executions().GetPreviewAsync(actor, line.Id);
        Assert.Equal("موظف تغيّر الدور", preview.EmployeeName);
    }
}
