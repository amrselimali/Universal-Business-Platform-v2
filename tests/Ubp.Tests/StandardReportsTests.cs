using Dapper;
using Npgsql;
using Ubp.Core.Models;
using Ubp.Core.Results;
using Xunit;

namespace Ubp.Tests;

/// <summary>
/// إطار التقارير القياسية: جدول موحّد (أعمدة + صفوف) من استعلام واحد،
/// مع حراسة المفتاح والفترة وقصّ الفروع.
/// </summary>
public sealed class StandardReportsTests : IClassFixture<TestDb>
{
    private readonly TestDb _db;

    public StandardReportsTests(TestDb db) => _db = db;

    [Fact]
    public async Task Vat_summary_aggregates_posted_purchase_lines_by_period_and_rate()
    {
        var actor = await _db.AdminActorAsync();
        var branch = await _db.CreateBranchAsync("SR-B1", "فرع التقرير");
        var item = await _db.CreateItemAsync("SR-IT1", "صنف تقرير");
        var supplier = await _db.PartnerRepo.CreateAsync(PartnerKind.Supplier, new PartnerInput
        { Code = "SR-SUP1", NameAr = "مورد التقرير" });

        await using var connection = new NpgsqlConnection(TestDb.AdminConnection);
        await connection.OpenAsync();
        var docId = Guid.CreateVersion7();
        await connection.ExecuteAsync("""
            INSERT INTO commercial_documents
                (id, branch_id, doc_date, doc_type, status, total, supplier_id)
            VALUES (@docId, @branch, current_date, 'purchase_invoice', 'draft', 115, @supplier)
            """, new { docId, branch, supplier = supplier.Id });
        await connection.ExecuteAsync("""
            INSERT INTO commercial_document_lines
                (document_id, line_no, item_id, unit_id, quantity, unit_price,
                 net_amount, tax_amount, total_amount)
            VALUES (@docId, 1, @item, @unit, 1, 100, 100, 15, 115)
            """, new { docId, item = item.Id, unit = item.BaseUnitId });
        await connection.ExecuteAsync(
            "UPDATE commercial_documents SET status = 'posted', posted_at = now() WHERE id = @docId",
            new { docId });

        var table = await _db.StandardReports().RunAsync(actor, StandardReportKeys.Vat,
            new ReportFilter(DateTime.Today, DateTime.Today, new[] { branch }));

        Assert.NotEmpty(table.Columns);
        var row = Assert.Single(table.Rows);
        Assert.Equal("100.00", row[3]); // الصافي
        Assert.Equal("15.00", row[4]);  // الضريبة
        Assert.Equal("115.00", row[5]); // الإجمالي
    }

    [Fact]
    public async Task Unknown_report_key_and_reversed_dates_are_rejected()
    {
        var actor = await _db.AdminActorAsync();
        var service = _db.StandardReports();

        var unknown = await Assert.ThrowsAsync<ValidationAppException>(async () =>
            await service.RunAsync(actor, "reports.unknown",
                new ReportFilter(DateTime.Today, DateTime.Today)));
        Assert.Equal("validation.report_key_unknown", unknown.Code);

        var range = await Assert.ThrowsAsync<ValidationAppException>(async () =>
            await service.RunAsync(actor, StandardReportKeys.Vat,
                new ReportFilter(DateTime.Today, DateTime.Today.AddDays(-5))));
        Assert.Equal("validation.date_range", range.Code);
    }

    [Fact]
    public async Task Reservation_status_reports_open_reservations_with_their_value()
    {
        var actor = await _db.AdminActorAsync();
        var branch = await _db.CreateBranchAsync("SR-B2", "فرع الحجوزات");
        var warehouse = await _db.CreateWarehouseAsync("SR-W2", "مخزن الحجوزات", branch);
        var item = await _db.CreateItemAsync("SR-IT2", "صنف محجوز");
        await _db.PostReceiptAsync(item, branch, warehouse, 5, 10);
        var customer = await _db.PartnerRepo.CreateAsync(PartnerKind.Customer, new PartnerInput
        { Code = "SR-C2", NameAr = "عميل الحجز" });

        var booking = await _db.BookingService().CreateAsync(actor, new BookingInput
        {
            BranchId = branch, BookingDate = DateTime.Today, CustomerId = customer.Id,
            Lines =
            {
                new BookingLineInput
                {
                    ItemId = item.Id, UnitId = item.BaseUnitId, WarehouseId = warehouse,
                    Quantity = 2, UnitPrice = 50
                }
            }
        });
        await _db.BookingService().ConfirmAsync(actor, booking.Id);

        var table = await _db.StandardReports().RunAsync(actor,
            StandardReportKeys.Reservations,
            new ReportFilter(DateTime.Today.AddDays(-1), DateTime.Today.AddDays(1), new[] { branch }));

        var row = Assert.Single(table.Rows);
        Assert.Equal("عميل الحجز", row[0]);
        Assert.Equal("2", row[3]);     // محجوز
        Assert.Equal("0", row[4]);     // مستهلك
        Assert.Equal("100.00", row[6]); // قيمة المتبقي
    }
}
