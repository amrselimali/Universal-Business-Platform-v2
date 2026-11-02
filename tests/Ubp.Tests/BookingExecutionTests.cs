using Dapper;
using Npgsql;
using Ubp.Core.Models;
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
                "UPDATE items SET metered_asset_id = @asset WHERE id = @item",
                new { asset = assetId, item = item.Id });
        }

        var booking = await _db.BookingService().CreateAsync(actor, new BookingInput
        {
            BranchId = branch, BookingDate = DateTime.Today, CustomerId = customer.Id,
            ExpiresAt = DateTimeOffset.UtcNow.AddMonths(6),
            Lines = { new BookingLineInput { ItemId = item.Id, UnitId = item.BaseUnitId,
                WarehouseId = warehouse, Quantity = 5, UnitPrice = 100 } }
        });
        await _db.BookingService().ConfirmAsync(actor, booking.Id);
        var line = (await _db.BookingService().GetAsync(actor, booking.Id)).Lines.Single();

        var start = new DateTimeOffset(DateTime.UtcNow.Date.AddHours(10), TimeSpan.Zero);
        await _db.AppointmentRepo.CreateAsync(Guid.CreateVersion7(), new AppointmentInput
        {
            BranchId = branch, EmployeeId = employeeId, BookingId = booking.Id,
            CustomerId = customer.Id, StartsAt = start, EndsAt = start.AddMinutes(30)
        }, actor.UserId);

        var preview = await _db.Executions().GetPreviewAsync(actor, line.Id);
        Assert.Equal(5m, preview.RemainingQty);
        Assert.Equal(assetId, preview.MeteredAssetId);
        Assert.Equal("موظف تنفيذ", preview.EmployeeName);
        Assert.Equal("عدد الوحدات", preview.UnitsColumnLabel);

        var result = await _db.Executions().ExecuteAsync(actor, new BookingExecutionInput
        { BookingLineId = line.Id, Quantity = 2, MeterUnits = 3 });

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
}
