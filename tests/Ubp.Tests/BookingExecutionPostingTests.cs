using Dapper;
using Npgsql;
using Ubp.Core.Models;
using Xunit;

namespace Ubp.Tests;

/// <summary>ترحيل الكميات المنفذة: إيراد الخدمة وتكلفة الأصناف المخزنية بكارتة العميل.</summary>
public sealed class BookingExecutionPostingTests : IClassFixture<TestDb>
{
    private readonly TestDb _db;

    public BookingExecutionPostingTests(TestDb db) => _db = db;

    [Fact]
    public async Task Service_execution_posts_revenue_and_updates_customer_statements()
    {
        var actor = await _db.AdminActorAsync();
        var chart = await _db.SeedAccountingAsync();
        var branch = await _db.CreateBranchAsync("EX-B-4", "فرع إيراد التنفيذ");
        var warehouse = await _db.CreateWarehouseAsync("EX-W-4", "مخزن إيراد التنفيذ", branch);
        var unit = await _db.UnitRepo.CreateAsync(new UnitInput
        { Code = "EX-U-4", NameAr = "وحدة إيراد التنفيذ" });
        var service = await _db.ItemRepo.CreateAsync(new ItemInput
        {
            Code = "EX-SV-4", NameAr = "خدمة إيراد التنفيذ", ItemType = "SERVICE",
            BaseUnitId = unit.Id,
            Units = new List<ItemUnitInput> { new() { UnitId = unit.Id, ConversionFactor = 1 } }
        });
        var customer = await _db.PartnerRepo.CreateAsync(PartnerKind.Customer, new PartnerInput
        { Code = "EX-C-4", NameAr = "عميل إيراد التنفيذ" });

        var employeeId = Guid.CreateVersion7();
        await _db.EmployeeRepo.CreateAsync(employeeId, new EmployeeInput
        { Code = "EX-E-4", NameAr = "موظف إيراد التنفيذ", IsServiceExecutor = true }, actor.UserId);
        var appointmentId = Guid.CreateVersion7();
        var start = new DateTimeOffset(DateTime.UtcNow.Date.AddHours(12), TimeSpan.Zero);
        await _db.AppointmentRepo.CreateAsync(appointmentId, new AppointmentInput
        {
            BranchId = branch, EmployeeId = employeeId, CustomerId = customer.Id,
            StartsAt = start, EndsAt = start.AddHours(1)
        }, actor.UserId);
        var appointment = (await _db.AppointmentRepo.GetAsync(appointmentId))!;
        await _db.AppointmentRepo.SetStatusAsync(appointmentId, appointment.Version,
            AppointmentStatuses.Confirmed, actor.UserId);

        var booking = await _db.BookingService().CreateAsync(actor, new BookingInput
        {
            BranchId = branch, BookingDate = DateTime.Today, CustomerId = customer.Id,
            AssignedEmployeeId = employeeId,
            Lines =
            {
                new BookingLineInput
                {
                    ItemId = service.Id, UnitId = service.BaseUnitId, WarehouseId = warehouse,
                    Quantity = 2, UnitPrice = 150
                }
            }
        });
        await _db.BookingRepo.ConfirmAsync(booking.Id, actor.UserId, reserveStock: false);
        var line = (await _db.BookingService().GetAsync(actor, booking.Id)).Lines.Single();

        var result = await _db.Executions().ExecuteAsync(actor, new BookingExecutionInput
        { BookingLineId = line.Id, Quantity = 2 });
        Assert.Equal(0m, result.CostTotal);

        await using var connection = new NpgsqlConnection(TestDb.AdminConnection);
        await connection.OpenAsync();
        var entries = (await connection.QueryAsync<(string Code, decimal Debit, decimal Credit)>("""
            SELECT a.code, l.debit, l.credit FROM journal_entry_lines l
            JOIN accounts a ON a.id = l.account_id
            JOIN journal_entries e ON e.id = l.entry_id
            WHERE e.source_type = 'execution' AND e.reference = @docNo
              AND e.description LIKE 'إيراد تنفيذ خدمة%'
            """, new { docNo = result.DocNo })).ToList();

        Assert.Contains(entries, l => l.Code == chart["1110"].Code && l.Debit == 300m);
        Assert.Contains(entries, l => l.Code == chart["4100"].Code && l.Credit == 300m);

        // كشف حساب العميل (قيمة): رصيد الطرف من القيد المرحّل.
        Assert.Equal(300m, await connection.ExecuteScalarAsync<decimal>(
            "SELECT COALESCE(SUM(debit - credit), 0) FROM party_balances " +
            "WHERE party_type = 'C' AND party_id = @id", new { id = customer.Id }));

        // كشف حساب العميل (كميات): المنفذ ظاهر والمتبقي صفر.
        var statement = await connection.QuerySingleAsync<(decimal Consumed, decimal Remaining)>(
            "SELECT consumed_qty, remaining_qty FROM v_customer_reservations " +
            "WHERE booking_line_id = @id", new { id = line.Id });
        Assert.Equal(2m, statement.Consumed);
        Assert.Equal(0m, statement.Remaining);
    }

    [Fact]
    public async Task Goods_execution_cost_uses_the_item_category_accounts()
    {
        var actor = await _db.AdminActorAsync();
        var chart = await _db.SeedAccountingAsync();
        var branch = await _db.CreateBranchAsync("EX-B-5", "فرع تكلفة التصنيف");
        var warehouse = await _db.CreateWarehouseAsync("EX-W-5", "مخزن تكلفة التصنيف", branch);
        var item = await _db.CreateItemAsync("EX-IT-5", "صنف تكلفة التصنيف");
        await _db.PostReceiptAsync(item, branch, warehouse, 10, 50);

        var category = await _db.ItemCategoryRepo.CreateAsync(new ItemCategoryInput
        { Code = "EX-CAT-5", NameAr = "تصنيف تكلفة التنفيذ" });
        await _db.AccountingSettings().SaveCategoryAccountAsync(actor, new ItemCategoryAccountInput
        {
            CategoryId = category.Id,
            InventoryAccountId = chart["1120"].Id,
            CogsAccountId = chart["5420"].Id
        });
        await using (var setup = new NpgsqlConnection(TestDb.AdminConnection))
        {
            await setup.OpenAsync();
            await setup.ExecuteAsync("UPDATE items SET category_id = @category WHERE id = @item",
                new { category = category.Id, item = item.Id });
        }

        var customer = await _db.PartnerRepo.CreateAsync(PartnerKind.Customer, new PartnerInput
        { Code = "EX-C-5", NameAr = "عميل تكلفة التصنيف" });
        var booking = await _db.BookingService().CreateAsync(actor, new BookingInput
        {
            BranchId = branch, BookingDate = DateTime.Today, CustomerId = customer.Id,
            Lines =
            {
                new BookingLineInput
                {
                    ItemId = item.Id, UnitId = item.BaseUnitId, WarehouseId = warehouse,
                    Quantity = 2, UnitPrice = 100
                }
            }
        });
        await _db.BookingService().ConfirmAsync(actor, booking.Id);
        var line = (await _db.BookingService().GetAsync(actor, booking.Id)).Lines.Single();

        var result = await _db.Executions().ExecuteAsync(actor, new BookingExecutionInput
        { BookingLineId = line.Id, Quantity = 2 });
        Assert.Equal(100m, result.CostTotal);

        await using var connection = new NpgsqlConnection(TestDb.AdminConnection);
        await connection.OpenAsync();
        var entries = (await connection.QueryAsync<(string Code, decimal Debit, decimal Credit)>("""
            SELECT a.code, l.debit, l.credit FROM journal_entry_lines l
            JOIN accounts a ON a.id = l.account_id
            JOIN journal_entries e ON e.id = l.entry_id
            WHERE e.source_type = 'execution' AND e.reference = @docNo
              AND e.description LIKE 'تكلفة تنفيذ%'
            """, new { docNo = result.DocNo })).ToList();

        Assert.Contains(entries, l => l.Code == chart["5420"].Code && l.Debit == 100m);
        Assert.Contains(entries, l => l.Code == chart["1120"].Code && l.Credit == 100m);
        Assert.DoesNotContain(entries, l => l.Code == chart["5100"].Code);
    }
}
