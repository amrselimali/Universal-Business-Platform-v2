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
}
