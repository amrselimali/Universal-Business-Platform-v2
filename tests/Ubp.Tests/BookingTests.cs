using Dapper;
using Npgsql;
using Ubp.Core.Models;
using Ubp.Core.Paging;
using Ubp.Core.Results;
using Xunit;

namespace Ubp.Tests;

public sealed class BookingTests : IClassFixture<TestDb>
{
    private readonly TestDb _db;

    public BookingTests(TestDb db) => _db = db;

    private async Task<(Guid Branch, Guid Warehouse, Item Item, Partner Customer, Employee Employee)>
        SetupAsync(string suffix)
    {
        var actor = await _db.AdminActorAsync();
        await _db.SeedAccountingAsync();
        var branch = await _db.CreateBranchAsync("BK-B-" + suffix, "فرع حجز " + suffix);
        var warehouse = await _db.CreateWarehouseAsync("BK-W-" + suffix, "مخزن " + suffix, branch);
        var item = await _db.CreateItemAsync("BK-IT-" + suffix, "صنف " + suffix);
        await _db.PostReceiptAsync(item, branch, warehouse, 10, 50);

        var customer = await _db.PartnerRepo.CreateAsync(PartnerKind.Customer, new PartnerInput
        { Code = "BK-C-" + suffix, NameAr = "عميل " + suffix });

        var employeeId = Guid.CreateVersion7();
        await _db.EmployeeRepo.CreateAsync(employeeId, new EmployeeInput
        {
            Code = "BK-E-" + suffix, NameAr = "موظف " + suffix,
            IsSeller = true, IsServiceExecutor = true
        }, actor.UserId);
        var employee = (await _db.EmployeeRepo.GetAsync(employeeId))!;
        return (branch, warehouse, item, customer, employee);
    }

    private async Task<Booking> CreateBookingAsync(Guid branch, Guid warehouse, Partner customer,
        Item item, decimal quantity)
    {
        var actor = await _db.AdminActorAsync();
        return await _db.BookingService().CreateAsync(actor, new BookingInput
        {
            BranchId = branch, BookingDate = DateTime.Today, CustomerId = customer.Id,
            Lines =
            {
                new BookingLineInput
                {
                    ItemId = item.Id, UnitId = item.BaseUnitId, WarehouseId = warehouse,
                    Quantity = quantity, UnitPrice = 100
                }
            }
        });
    }

    [Fact]
    public async Task Booking_reserves_available_quantity_and_blocks_overbooking()
    {
        var actor = await _db.AdminActorAsync();
        var service = _db.BookingService();
        var (branch, warehouse, item, customer, _) = await SetupAsync("1");

        var first = await CreateBookingAsync(branch, warehouse, customer, item, 6);
        var confirmed = await service.ConfirmAsync(actor, first.Id);

        Assert.Equal(BookingStatuses.Confirmed, confirmed.Status);
        Assert.StartsWith("BK-", confirmed.DocNo);
        Assert.Equal(4m, await service.AvailableAsync(actor, item.Id, warehouse));

        var second = await CreateBookingAsync(branch, warehouse, customer, item, 9);
        var error = await Assert.ThrowsAsync<ValidationAppException>(() =>
            service.ConfirmAsync(actor, second.Id));
        Assert.Equal("validation.booking_not_available", error.Code);
    }

    [Fact]
    public async Task Booking_times_with_non_utc_offset_are_stored_as_utc()
    {
        var actor = await _db.AdminActorAsync();
        var service = _db.BookingService();
        var (branch, warehouse, item, customer, _) = await SetupAsync("8");
        var localStart = new DateTimeOffset(
            DateTime.SpecifyKind(DateTime.Today.AddHours(10), DateTimeKind.Unspecified),
            TimeSpan.FromHours(3));

        var booking = await service.CreateAsync(actor, new BookingInput
        {
            BranchId = branch, CustomerId = customer.Id, BookingDate = DateTime.Today,
            StartsAt = localStart, EndsAt = localStart.AddMinutes(30),
            Lines = { new BookingLineInput { ItemId = item.Id, UnitId = item.BaseUnitId,
                WarehouseId = warehouse, Quantity = 1, UnitPrice = 0 } }
        });

        var loaded = await service.GetAsync(actor, booking.Id);
        Assert.Equal(localStart.ToUniversalTime(), loaded.StartsAt);
        Assert.Equal(localStart.AddMinutes(30).ToUniversalTime(), loaded.EndsAt);
    }

    [Fact]
    public async Task Booking_list_filters_by_selected_day()
    {
        var actor = await _db.AdminActorAsync();
        var service = _db.BookingService();
        var (branch, warehouse, item, customer, _) = await SetupAsync("6");

        var today = await CreateBookingAsync(branch, warehouse, customer, item, 1);
        var yesterday = await service.CreateAsync(actor, new BookingInput
        {
            BranchId = branch, CustomerId = customer.Id,
            BookingDate = DateTime.Today.AddDays(-1),
            Lines = { new BookingLineInput { ItemId = item.Id, UnitId = item.BaseUnitId,
                WarehouseId = warehouse, Quantity = 1, UnitPrice = 10 } }
        });

        var list = await service.ListAsync(actor, new BookingFilter
        { BranchId = branch, Date = DateTime.Today }, new PageRequest { Page = 1, PageSize = 25 });

        Assert.Contains(list.Items, b => b.Id == today.Id);
        Assert.DoesNotContain(list.Items, b => b.Id == yesterday.Id);
    }

    [Fact]
    public async Task Delivery_posts_stock_and_cogs_and_completes_booking()
    {
        var actor = await _db.AdminActorAsync();
        var fulfillment = _db.Fulfillment();
        var (branch, warehouse, item, customer, _) = await SetupAsync("2");

        var booking = await CreateBookingAsync(branch, warehouse, customer, item, 10);
        await _db.BookingService().ConfirmAsync(actor, booking.Id);
        var line = booking.Lines.Single();

        var note = await fulfillment.CreateDeliveryAsync(actor, new DeliveryInput
        {
            BookingId = booking.Id, BranchId = branch, DocDate = DateTime.Today,
            WarehouseId = warehouse,
            Lines = { new DeliveryLineInput { BookingLineId = line.Id, Quantity = 10 } }
        });

        var posted = await fulfillment.PostDeliveryAsync(actor, note.Id);

        Assert.Equal(DeliveryStatuses.Posted, posted.Status);
        Assert.StartsWith("DN-", posted.DocNo);
        Assert.NotNull(posted.StockDocumentId);
        Assert.NotNull(posted.JournalEntryId);
        Assert.Equal(500m, posted.CostTotal);
        Assert.Equal(0m, await _db.OnHandAsync(item.Id, warehouse));
        Assert.Equal(BookingStatuses.Completed,
            (await _db.BookingService().GetAsync(actor, booking.Id)).Status);

        await using var connection = new NpgsqlConnection(TestDb.AdminConnection);
        await connection.OpenAsync();
        var entry = await connection.QuerySingleAsync<(decimal Debit, decimal Credit, string Status)>(
            "SELECT total_debit, total_credit, status FROM journal_entries WHERE id = @id",
            new { id = posted.JournalEntryId });
        Assert.Equal(500m, entry.Debit);
        Assert.Equal(entry.Debit, entry.Credit);
        Assert.Equal("posted", entry.Status);
    }

    [Fact]
    public async Task Cancelled_booking_releases_reservation()
    {
        var actor = await _db.AdminActorAsync();
        var service = _db.BookingService();
        var (branch, warehouse, item, customer, _) = await SetupAsync("3");

        var booking = await CreateBookingAsync(branch, warehouse, customer, item, 6);
        await service.ConfirmAsync(actor, booking.Id);
        Assert.Equal(4m, await service.AvailableAsync(actor, item.Id, warehouse));

        var cancelled = await service.CancelAsync(actor, booking.Id, "إلغاء بطلب العميل");
        Assert.Equal(BookingStatuses.Cancelled, cancelled.Status);
        Assert.Equal(10m, await service.AvailableAsync(actor, item.Id, warehouse));
    }

    [Fact]
    public async Task Appointment_overlap_is_rejected_and_cancel_frees_slot()
    {
        var actor = await _db.AdminActorAsync();
        var service = _db.Fulfillment();
        var (branch, _, _, customer, employee) = await SetupAsync("4");
        var start = DateTimeOffset.Now.Date.AddHours(10);

        var first = await service.CreateAppointmentAsync(actor, new AppointmentInput
        {
            BranchId = branch, EmployeeId = employee.Id, CustomerId = customer.Id,
            StartsAt = start, EndsAt = start.AddMinutes(30)
        });

        var overlap = await Assert.ThrowsAsync<ValidationAppException>(() =>
            service.CreateAppointmentAsync(actor, new AppointmentInput
            {
                BranchId = branch, EmployeeId = employee.Id,
                StartsAt = start.AddMinutes(15), EndsAt = start.AddMinutes(45)
            }));
        Assert.Equal("validation.appointment_overlap", overlap.Code);

        await service.SetAppointmentStatusAsync(actor, first.Id, first.Version,
            AppointmentStatuses.Cancelled);

        var again = await service.CreateAppointmentAsync(actor, new AppointmentInput
        {
            BranchId = branch, EmployeeId = employee.Id,
            StartsAt = start.AddMinutes(15), EndsAt = start.AddMinutes(45)
        });
        Assert.NotEqual(Guid.Empty, again.Id);
    }

    [Fact]
    public async Task Assignment_history_is_recorded()
    {
        var actor = await _db.AdminActorAsync();
        var service = _db.BookingService();
        var (branch, warehouse, item, customer, employee) = await SetupAsync("5");

        var secondEmployeeId = Guid.CreateVersion7();
        await _db.EmployeeRepo.CreateAsync(secondEmployeeId, new EmployeeInput
        { Code = "BK-E5B", NameAr = "موظف ثانٍ", IsServiceExecutor = true }, actor.UserId);

        var booking = await CreateBookingAsync(branch, warehouse, customer, item, 1);
        await service.AssignAsync(actor, booking.Id, employee.Id, "owner");
        var updated = await service.AssignAsync(actor, booking.Id, secondEmployeeId, "owner");

        Assert.Equal(secondEmployeeId, updated.AssignedEmployeeId);
        Assert.Equal(2, updated.Assignments.Count);
        Assert.Single(updated.Assignments, a => a.UnassignedAt is null);
    }
}
