using Dapper;
using Npgsql;
using Ubp.Core.Models;
using Ubp.Core.Results;
using Xunit;

namespace Ubp.Tests;

public sealed class BookingExecutionTests : IClassFixture<TestDb>
{
    private readonly TestDb _db;

    public BookingExecutionTests(TestDb db) => _db = db;

    [Fact]
    public async Task Execution_posts_stock_and_adds_meter_units_then_reset_clears_them()
    {
        var actor = await _db.AdminActorAsync();
        await _db.SeedAccountingAsync();
        var branch = await _db.CreateBranchAsync("EX-B-1", "فرع تنفيذ");
        var warehouse = await _db.CreateWarehouseAsync("EX-W-1", "مخزن تنفيذ", branch);
        var item = await _db.CreateItemAsync("EX-IT-1", "صنف تنفيذ");
        await _db.PostReceiptAsync(item, branch, warehouse, 10, 50);
        var customer = await _db.PartnerRepo.CreateAsync(PartnerKind.Customer, new PartnerInput
        { Code = "EX-C-1", NameAr = "عميل تنفيذ" });

        var employeeId = Guid.CreateVersion7();
        await _db.EmployeeRepo.CreateAsync(employeeId, new EmployeeInput
        { Code = "EX-E-1", NameAr = "موظف تنفيذ", IsSeller = true }, actor.UserId);

        var categoryId = Guid.CreateVersion7();
        var assetId = Guid.CreateVersion7();
        await using (var connection = new NpgsqlConnection(TestDb.AdminConnection))
        {
            await connection.OpenAsync();
            await connection.ExecuteAsync("""
                INSERT INTO asset_categories
                    (id, code, name_ar, depreciation_method, useful_life_months, is_metered)
                VALUES (@id, 'EX-AC-1', 'تصنيف عدّاد', 'LINEAR', 60, true)
                """, new { id = categoryId });
            await connection.ExecuteAsync("""
                INSERT INTO fixed_assets
                    (id, code, name_ar, category_id, branch_id, purchase_date, cost, is_metered,
                     meter_type, current_meter_value)
                VALUES (@id, 'EX-AS-1', 'ماكينة عدّاد', @category, @branch, current_date, 1000,
                        true, 'UNITS', 0)
                """, new { id = assetId, category = categoryId, branch });
            await connection.ExecuteAsync(
                "UPDATE items SET is_metered = true WHERE id = @item",
                new { item = item.Id });
        }

        var booking = await _db.BookingService().CreateAsync(actor, new BookingInput
        {
            BranchId = branch, BookingDate = DateTime.Today, CustomerId = customer.Id,
            Lines = { new BookingLineInput { ItemId = item.Id, UnitId = item.BaseUnitId,
                WarehouseId = warehouse, Quantity = 5, UnitPrice = 100 } }
        });
        await _db.BookingService().ConfirmAsync(actor, booking.Id);
        var line = (await _db.BookingService().GetAsync(actor, booking.Id)).Lines.Single();

        var start = new DateTimeOffset(DateTime.UtcNow.Date.AddHours(10), TimeSpan.Zero);
        var appointmentId = Guid.CreateVersion7();
        await _db.AppointmentRepo.CreateAsync(appointmentId, new AppointmentInput
        {
            BranchId = branch, EmployeeId = employeeId,
            CustomerId = customer.Id, StartsAt = start, EndsAt = start.AddMinutes(30)
        }, actor.UserId);
        var appointment = (await _db.AppointmentRepo.GetAsync(appointmentId))!;
        await _db.AppointmentRepo.SetStatusAsync(appointmentId, appointment.Version,
            AppointmentStatuses.Confirmed, actor.UserId);

        var preview = await _db.Executions().GetPreviewAsync(actor, line.Id);
        Assert.Equal(5m, preview.RemainingQty);
        Assert.True(preview.ItemIsMetered);
        Assert.Contains(preview.MeterAssets, a => a.Id == assetId);
        Assert.Equal("موظف تنفيذ", preview.EmployeeName);
        Assert.Equal("عدد الوحدات", preview.UnitsColumnLabel);

        var result = await _db.Executions().ExecuteAsync(actor, new BookingExecutionInput
        {
            BookingLineId = line.Id, Quantity = 2, MeteredAssetId = assetId, MeterUnits = 3
        });

        Assert.Equal(2m, result.Quantity);
        Assert.Equal(3m, result.MeterValueAfter);
        Assert.Equal(100m, result.CostTotal);
        Assert.Equal(8m, await _db.OnHandAsync(item.Id, warehouse));

        var after = await _db.BookingService().GetAsync(actor, booking.Id);
        Assert.Equal(2m, after.Lines.Single().DeliveredQty);
        Assert.Equal(BookingStatuses.PartiallyDelivered, after.Status);

        await using (var connection = new NpgsqlConnection(TestDb.AdminConnection))
        {
            await connection.OpenAsync();
            Assert.Equal(3m, await connection.ExecuteScalarAsync<decimal>(
                "SELECT current_meter_value FROM fixed_assets WHERE id = @id", new { id = assetId }));
            Assert.Equal(3m, await connection.ExecuteScalarAsync<decimal>(
                "SELECT delta FROM asset_meter_readings WHERE source_line_id IS NOT NULL " +
                "AND asset_id = @id ORDER BY reading_at DESC LIMIT 1", new { id = assetId }));
            Assert.Equal(1, await connection.ExecuteScalarAsync<int>(
                "SELECT count(*)::int FROM booking_execution_lines WHERE booking_line_id = @id",
                new { id = line.Id }));
        }

        await _db.Assets().ResetMeterAsync(actor, assetId, "تصفير اختبار");
        await using var check = new NpgsqlConnection(TestDb.AdminConnection);
        await check.OpenAsync();
        Assert.Equal(0m, await check.ExecuteScalarAsync<decimal>(
            "SELECT current_meter_value FROM fixed_assets WHERE id = @id", new { id = assetId }));
    }

    [Fact]
    public async Task Execution_employee_requires_a_confirmed_customer_appointment()
    {
        var actor = await _db.AdminActorAsync();
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

        var booking = await _db.BookingService().CreateAsync(actor, new BookingInput
        {
            BranchId = branch, BookingDate = DateTime.Today, CustomerId = customer.Id,
            AssignedEmployeeId = employeeId,
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

        // الموعد المجدول (غير المؤكد) لا يُعتمد: لا موظف مسؤول حتى لو كان مسنداً على الحجز.
        var pending = await _db.Executions().GetPreviewAsync(actor, line.Id);
        Assert.Null(pending.EmployeeName);
        var blocked = await Assert.ThrowsAsync<ValidationAppException>(() =>
            _db.Executions().ExecuteAsync(actor, new BookingExecutionInput
            { BookingLineId = line.Id, Quantity = 1 }));
        Assert.Equal("validation.execution_employee_required", blocked.Code);

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
    public async Task Metered_item_executes_without_a_meter_reading()
    {
        var actor = await _db.AdminActorAsync();
        await _db.SeedAccountingAsync();
        var branch = await _db.CreateBranchAsync("EX-B-3", "فرع تنفيذ بلا عدّاد");
        var warehouse = await _db.CreateWarehouseAsync("EX-W-3", "مخزن تنفيذ بلا عدّاد", branch);
        var item = await _db.CreateItemAsync("EX-IT-3", "صنف تنفيذ بلا عدّاد");
        await _db.PostReceiptAsync(item, branch, warehouse, 10, 50);
        var customer = await _db.PartnerRepo.CreateAsync(PartnerKind.Customer, new PartnerInput
        { Code = "EX-C-3", NameAr = "عميل تنفيذ بلا عدّاد" });

        await using (var connection = new NpgsqlConnection(TestDb.AdminConnection))
        {
            await connection.OpenAsync();
            await connection.ExecuteAsync(
                "UPDATE items SET is_metered = true WHERE id = @item", new { item = item.Id });
        }

        var booking = await _db.BookingService().CreateAsync(actor, new BookingInput
        {
            BranchId = branch, BookingDate = DateTime.Today, CustomerId = customer.Id,
            Lines =
            {
                new BookingLineInput
                {
                    ItemId = item.Id, UnitId = item.BaseUnitId, WarehouseId = warehouse,
                    Quantity = 4, UnitPrice = 100
                }
            }
        });
        await _db.BookingService().ConfirmAsync(actor, booking.Id);
        var line = (await _db.BookingService().GetAsync(actor, booking.Id)).Lines.Single();

        // قراءة العدّاد للمتابعة في الصيانة فقط: غيابها لا يمنع تنفيذ البيع.
        var preview = await _db.Executions().GetPreviewAsync(actor, line.Id);
        Assert.True(preview.ItemIsMetered);

        var result = await _db.Executions().ExecuteAsync(actor, new BookingExecutionInput
        { BookingLineId = line.Id, Quantity = 2 });
        Assert.Equal(2m, result.Quantity);
        Assert.Equal(0m, result.MeterUnits);
        Assert.Equal(0m, result.MeterValueAfter);

        await using var check = new NpgsqlConnection(TestDb.AdminConnection);
        await check.OpenAsync();
        Assert.Equal(0, await check.ExecuteScalarAsync<int>(
            "SELECT count(*)::int FROM asset_meter_readings WHERE source_line_id IN " +
            "(SELECT id FROM booking_execution_lines WHERE booking_line_id = @id)",
            new { id = line.Id }));
        Assert.Equal(8m, await _db.OnHandAsync(item.Id, warehouse));
    }
}
