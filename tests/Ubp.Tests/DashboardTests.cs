using Dapper;
using Npgsql;
using Ubp.Core.Models;
using Ubp.Core.Results;
using Xunit;

namespace Ubp.Tests;

/// <summary>
/// 6C: إجماليات المبيعات اليومية تُحدَّث داخل معاملة الترحيل، وتتراجع معه، ولوحة المؤشرات تقرأ منها.
/// </summary>
public sealed class DashboardTests : IClassFixture<TestDb>
{
    private readonly TestDb _db;

    public DashboardTests(TestDb db) => _db = db;

    private sealed record Fixture(Guid Branch, Guid Warehouse, Item Item, Partner Customer,
        Booking Booking, Guid BookingLineId);

    private async Task<Fixture> SetupAsync(string suffix)
    {
        var actor = await _db.AdminActorAsync();
        var settings = await _db.Settings().GetAsync();
        settings.AutoInvoiceOnDelivery = false;
        await _db.Settings().UpdateAsync(actor, settings);
        var chart = await _db.SeedAccountingAsync();
        await _db.AccountingSettings().SetDefaultAsync(actor, PostingKeys.CustomerAdvances,
            chart["1110"].Id);

        var branch = await _db.CreateBranchAsync("DB-B-" + suffix, "فرع اللوحة " + suffix);
        var warehouse = await _db.CreateWarehouseAsync("DB-W-" + suffix, "مخزن اللوحة " + suffix, branch);
        var item = await _db.CreateItemAsync("DB-IT-" + suffix, "صنف اللوحة " + suffix);
        await _db.PostReceiptAsync(item, branch, warehouse, 10, 50);
        var customer = await _db.PartnerRepo.CreateAsync(PartnerKind.Customer, new PartnerInput
        { Code = "DB-C-" + suffix, NameAr = "عميل اللوحة " + suffix });

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
        var bookingLineId = (await _db.BookingService().GetAsync(actor, booking.Id)).Lines.Single().Id;

        var note = await _db.Fulfillment().CreateDeliveryAsync(actor, new DeliveryInput
        {
            BookingId = booking.Id, BranchId = branch, DocDate = DateTime.Today,
            WarehouseId = warehouse,
            Lines = { new DeliveryLineInput { BookingLineId = bookingLineId, Quantity = 2 } }
        });
        await _db.Fulfillment().PostDeliveryAsync(actor, note.Id);
        return new Fixture(branch, warehouse, item, customer, booking, bookingLineId);
    }

    private async Task<Guid> CreateInvoiceAsync(Fixture f, decimal quantity)
    {
        var actor = await _db.AdminActorAsync();
        var draft = await _db.SalesInvoices().CreateAsync(actor, new CommercialDocumentInput
        {
            DocType = CommercialDocTypes.SalesInvoice, BranchId = f.Branch,
            CustomerId = f.Customer.Id, BookingId = f.Booking.Id, DocDate = DateTime.Today,
            Lines =
            {
                new CommercialDocumentLineInput
                {
                    ItemId = f.Item.Id, UnitId = f.Item.BaseUnitId, Quantity = quantity,
                    UnitPrice = 100, BookingLineId = f.BookingLineId
                }
            }
        });
        return draft.Header.Id;
    }

    [Fact]
    public async Task Posting_an_invoice_updates_the_daily_totals_and_the_dashboard()
    {
        var actor = await _db.AdminActorAsync();
        var fixture = await SetupAsync("1");
        var invoiceId = await CreateInvoiceAsync(fixture, 2);
        await _db.SalesInvoices().PostAsync(actor, invoiceId);

        await using var connection = new NpgsqlConnection(TestDb.AdminConnection);
        await connection.OpenAsync();
        var row = await connection.QuerySingleAsync<(int Invoices, decimal Net, decimal Gross, decimal Cogs)>("""
            SELECT invoice_count, net_total, gross_total, cogs_total
            FROM sales_daily_totals WHERE branch_id = @branch AND day = current_date
            """, new { branch = fixture.Branch });

        Assert.Equal(1, row.Invoices);
        Assert.Equal(200m, row.Net);
        Assert.Equal(200m, row.Gross);
        Assert.Equal(100m, row.Cogs);

        var snapshot = await _db.Dashboard().GetAsync(actor);
        Assert.True(snapshot.Month.NetTotal >= 200m);
        Assert.True(snapshot.Today.InvoiceCount >= 1);
        Assert.True(snapshot.StockValue >= 0m);
    }

    [Fact]
    public async Task Totals_roll_back_when_the_posting_fails()
    {
        var actor = await _db.AdminActorAsync();
        var fixture = await SetupAsync("2");
        var invoiceId = await CreateInvoiceAsync(fixture, 2);

        var period = await _db.PeriodRepo.CreateAsync(new FiscalPeriodInput
        {
            Code = "DBP" + Guid.NewGuid().ToString("N")[..6],
            NameAr = "فترة مغلقة للوحة",
            StartDate = DateTime.UtcNow.Date.AddDays(-1),
            EndDate = DateTime.UtcNow.Date.AddDays(1)
        });
        await _db.PeriodRepo.UpsertClosureAsync(period.Id, PeriodScopes.Company, null,
            PeriodModules.Accounting, true, actor.UserId, null);

        try
        {
            await Assert.ThrowsAsync<ValidationAppException>(async () =>
                await _db.SalesInvoices().PostAsync(actor, invoiceId));

            await using var connection = new NpgsqlConnection(TestDb.AdminConnection);
            await connection.OpenAsync();
            var rows = await connection.ExecuteScalarAsync<int>(
                "SELECT count(*)::int FROM sales_daily_totals WHERE branch_id = @branch",
                new { branch = fixture.Branch });
            Assert.Equal(0, rows);
        }
        finally
        {
            // الاختبارات تشترك في نفس القاعدة: نعيد فتح الفترة حتى لا نُفسد بقية الاختبارات.
            await _db.PeriodRepo.UpsertClosureAsync(period.Id, PeriodScopes.Company, null,
                PeriodModules.Accounting, false, actor.UserId, "إعادة فتح بعد الاختبار");
        }
    }
}
