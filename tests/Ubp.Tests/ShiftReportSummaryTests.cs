using Dapper;
using Npgsql;
using Ubp.Core.Models;
using Xunit;

namespace Ubp.Tests;

/// <summary>
/// ملخص تقرير الشفت: عدد عملاء بلا متابعة/حجز مستقبلي، وعدد حجوزات الغد المؤكدة فقط،
/// ومستخدم إقفال الشفت (اسم ملف تصدير PDF) — 0094.
/// </summary>
public sealed class ShiftReportSummaryTests : IClassFixture<TestDb>
{
    private readonly TestDb _db;

    public ShiftReportSummaryTests(TestDb db) => _db = db;

    [Fact]
    public async Task Report_counts_idle_customers_and_confirmed_tomorrow_bookings()
    {
        var actor = await _db.AdminActorAsync();
        await _db.SeedAccountingAsync();
        var branch = await _db.CreateBranchAsync("SRS-B", "فرع ملخص الشفت");
        var busy = await _db.PartnerRepo.CreateAsync(PartnerKind.Customer, new PartnerInput
        {
            Code = "SRS-C-1", NameAr = "عميل عنده حجز", ScopeType = "BRANCH", BranchId = branch
        });
        await _db.PartnerRepo.CreateAsync(PartnerKind.Customer, new PartnerInput
        {
            Code = "SRS-C-2", NameAr = "عميل بلا متابعة", ScopeType = "BRANCH", BranchId = branch
        });

        await using (var connection = new NpgsqlConnection(TestDb.AdminConnection))
        {
            await connection.OpenAsync();
            await connection.ExecuteAsync("""
                INSERT INTO bookings (id, branch_id, doc_no, booking_date, status, customer_id,
                    source, total_amount)
                VALUES (@confirmedId, @branch, 'SRS-BK-1', current_date + 1, 'confirmed',
                        @customerId, 'manual', 100),
                       (@cancelledId, @branch, 'SRS-BK-2', current_date + 1, 'cancelled',
                        @customerId, 'manual', 50)
                """, new
            {
                confirmedId = Guid.CreateVersion7(), cancelledId = Guid.CreateVersion7(),
                branch, customerId = busy.Id
            });
        }

        var shifts = _db.TreasuryService();
        var shift = await shifts.OpenShiftAsync(actor, new ShiftInput { BranchId = branch });
        var report = await shifts.GetShiftReportAsync(actor, shift.Id);

        // حجوزات الغد: المؤكد فقط (المُلغى مستبعد).
        Assert.Equal(1, report.TomorrowBookingsCount);
        // عميل واحد فقط بلا متابعة أو حجز مستقبلي.
        Assert.Equal(1, report.IdleCustomersCount);

        await shifts.CloseShiftAsync(actor, shift.Id, new List<ShiftCashCountInput>());
        var closed = await shifts.GetShiftReportAsync(actor, shift.Id);

        // مستخدم الإقفال يُحفظ ليُستخدم في اسم ملف التصدير الافتراضي.
        Assert.Equal("admin", closed.Shift.ClosedByName);
        Assert.NotNull(closed.Shift.ClosedAt);
    }
}
