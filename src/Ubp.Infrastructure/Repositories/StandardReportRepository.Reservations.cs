using Dapper;
using Ubp.Core.Models;
using Ubp.Infrastructure.Data;

namespace Ubp.Infrastructure.Repositories;

public sealed partial class StandardReportRepository
{
    /// <summary>حالة الحجوزات والدفعات: محجوز/مستهلك/متبقٍ + الدفعة (مصدره v_customer_reservations).</summary>
    public async Task<ReportTable> ReservationStatusAsync(DateOnly from, DateOnly to,
        Guid[] branchIds, bool onlyRemaining = true, CancellationToken ct = default)
    {
        const string sql = """
            SELECT c.name_ar AS Customer, r.booking_no AS BookingNo, i.code AS ItemCode,
                   r.reserved_qty AS Reserved, r.consumed_qty AS Consumed,
                   r.remaining_qty AS Remaining, r.remaining_value AS RemainingValue,
                   r.deposit_amount AS Deposit, r.expires_at AS ExpiresAt
            FROM v_customer_reservations r
            JOIN customers c ON c.id = r.customer_id
            JOIN items i ON i.id = r.item_id
            WHERE (@onlyRemaining = false OR r.remaining_qty > 0)
              AND (@allBranches OR r.branch_id = ANY(@branchIds))
              AND (r.expires_at IS NULL OR r.expires_at::date BETWEEN @from AND @to)
            ORDER BY r.expires_at NULLS LAST, c.name_ar
            LIMIT 500
            """;
        await using var connection = await _factory.OpenAsync(ct);
        var rows = (await connection.QueryAsync<OpenReservationRow>(new CommandDefinition(sql, new
        {
            from = from.ToDateTime(TimeOnly.MinValue), to = to.ToDateTime(TimeOnly.MinValue),
            branchIds, allBranches = branchIds.Length == 0, onlyRemaining
        }, cancellationToken: ct))).ToList();

        var columns = new List<ReportColumn>
        {
            new("customer", "report.customer"),
            new("booking", "reservations.booking"),
            new("item", "reservations.item"),
            new("reserved", "reservations.reserved", ReportValueType.Number),
            new("consumed", "reservations.consumed", ReportValueType.Number),
            new("remaining", "reservations.remaining", ReportValueType.Number),
            new("remaining_value", "reservations.remaining_value", ReportValueType.Money),
            new("deposit", "report.deposit", ReportValueType.Money),
            new("expires", "reservations.expires", ReportValueType.Date)
        };
        var data = rows.Select(r => (IReadOnlyList<string?>)
        [
            r.Customer, r.BookingNo, r.ItemCode, Num(r.Reserved), Num(r.Consumed), Num(r.Remaining),
            Money(r.RemainingValue), Money(r.Deposit),
            r.ExpiresAt?.ToLocalTime().ToString("yyyy-MM-dd")
        ]).ToList();

        return new ReportTable(columns, data,
            $"report.remaining_total={Money(rows.Sum(r => r.RemainingValue))}");
    }

    private sealed class OpenReservationRow
    {
        public string Customer { get; set; } = "";
        public string? BookingNo { get; set; }
        public string ItemCode { get; set; } = "";
        public decimal Reserved { get; set; }
        public decimal Consumed { get; set; }
        public decimal Remaining { get; set; }
        public decimal RemainingValue { get; set; }
        public decimal Deposit { get; set; }
        public DateTimeOffset? ExpiresAt { get; set; }
    }
}
