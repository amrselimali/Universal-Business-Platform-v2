using Dapper;
using Ubp.Core.Models;
using Ubp.Core.Results;

namespace Ubp.Infrastructure.Repositories;

public sealed partial class TreasuryRepository
{
    /// <summary>
    /// تقرير الوردية عند الإغلاق: كل تسجيلات الشيفت غير الملغاة (مسودة أو مرحّلة) —
    /// تحصيلات وإيرادات وحركة خزائن، وعملاء بلا متابعة/حجز مستقبلي، وحجوزات ومواعيد الغد.
    /// </summary>
    public async Task<ShiftReport> GetShiftReportAsync(Guid shiftId, CancellationToken ct = default)
    {
        var shift = await GetShiftAsync(shiftId, ct) ?? throw new NotFoundAppException();
        await using var connection = await _factory.OpenAsync(ct);
        if (shift.IsOpen)
        {
            var movement = await CashMovementAsync(connection, null, shift.BranchId,
                shift.OpenedAt, ct);
            shift.ExpectedCash = Math.Round(shift.OpeningCash + movement, 2,
                MidpointRounding.AwayFromZero);
        }
        var args = new
        {
            branchId = shift.BranchId, openedAt = shift.OpenedAt, until = shift.ClosedAt
        };

        var report = new ShiftReport { Shift = shift };

        report.Collections = (await connection.QueryAsync<ShiftReportLine>(new CommandDefinition("""
            SELECT v.id AS Id, v.doc_no AS DocNo, v.doc_date AS DocDate, v.party_name AS PartyName,
                   v.notes AS Description, v.status AS Status, v.total_amount AS Amount
            FROM vouchers v
            WHERE v.branch_id = @branchId AND v.voucher_type = 'receipt'
              AND v.status <> 'cancelled' AND v.created_at >= @openedAt
              AND (@until::timestamptz IS NULL OR v.created_at <= @until::timestamptz)
            ORDER BY v.doc_date, v.doc_no
            """, args, cancellationToken: ct))).ToList();

        report.Revenues = (await connection.QueryAsync<ShiftReportLine>(new CommandDefinition("""
            SELECT d.id AS Id, d.doc_no AS DocNo, d.doc_date AS DocDate, d.party_name AS PartyName,
                   d.notes AS Description, d.status AS Status, d.total AS Amount
            FROM commercial_documents d
            WHERE d.branch_id = @branchId AND d.doc_type = 'sales_invoice'
              AND d.status <> 'cancelled' AND d.created_at >= @openedAt
              AND (@until::timestamptz IS NULL OR d.created_at <= @until::timestamptz)
            ORDER BY d.doc_date, d.doc_no
            """, args, cancellationToken: ct))).ToList();

        report.Movements = (await connection.QueryAsync<ShiftMovementLine>(new CommandDefinition("""
            SELECT v.doc_no AS DocNo, v.voucher_type AS DocType, v.doc_date AS DocDate,
                   st.name_ar AS TreasuryName, tt.name_ar AS ToTreasuryName, v.status AS Status,
                   v.party_name AS PartyName, v.total_amount AS Amount,
                   (v.voucher_type = 'receipt') AS Inflow
            FROM vouchers v
            JOIN treasuries st ON st.id = v.treasury_id
            LEFT JOIN treasuries tt ON tt.id = v.to_treasury_id
            WHERE v.branch_id = @branchId AND v.status <> 'cancelled'
              AND v.created_at >= @openedAt
              AND (@until::timestamptz IS NULL OR v.created_at <= @until::timestamptz)
            UNION ALL
            SELECT v.doc_no, v.voucher_type, v.doc_date, tt.name_ar, NULL, v.status,
                   v.party_name, v.total_amount, true
            FROM vouchers v
            JOIN treasuries tt ON tt.id = v.to_treasury_id
            WHERE v.branch_id = @branchId AND v.voucher_type = 'transfer'
              AND v.status <> 'cancelled' AND v.created_at >= @openedAt
              AND (@until::timestamptz IS NULL OR v.created_at <= @until::timestamptz)
            ORDER BY 3, 1
            """, args, cancellationToken: ct))).ToList();

        report.IdleCustomers = (await connection.QueryAsync<ShiftCustomerRow>(new CommandDefinition("""
            SELECT c.id AS CustomerId, c.code AS Code, c.name_ar AS NameAr, c.phone AS Phone,
                   COALESCE(lb.last_at, la.last_at, lf.last_at) AS LastActivityAt,
                   CASE
                       WHEN lb.last_at IS NULL AND la.last_at IS NULL AND lf.last_at IS NULL
                           THEN NULL
                       WHEN lf.last_at IS NOT NULL
                            AND lf.last_at >= COALESCE(lb.last_at, '-infinity'::timestamptz)
                            AND lf.last_at >= COALESCE(la.last_at, '-infinity'::timestamptz)
                           THEN 'متابعة'
                       WHEN lb.last_at IS NOT NULL
                            AND lb.last_at >= COALESCE(la.last_at, '-infinity'::timestamptz)
                           THEN 'حجز'
                       ELSE 'موعد' END AS LastActivity
            FROM customers c
            LEFT JOIN LATERAL (
                SELECT max(COALESCE(b.starts_at, b.booking_date::timestamptz)) AS last_at
                FROM bookings b
                WHERE b.customer_id = c.id AND b.status NOT IN ('cancelled', 'reversed')
            ) lb ON true
            LEFT JOIN LATERAL (
                SELECT max(a.starts_at) AS last_at
                FROM appointments a
                WHERE a.customer_id = c.id AND a.status <> 'cancelled'
            ) la ON true
            LEFT JOIN LATERAL (
                SELECT max(f.due_at) AS last_at
                FROM follow_ups f
                WHERE f.customer_id = c.id
            ) lf ON true
            WHERE c.is_active AND c.branch_id = @branchId
              AND NOT EXISTS (
                  SELECT 1 FROM bookings b
                  WHERE b.customer_id = c.id AND b.status NOT IN ('cancelled', 'reversed')
                    AND COALESCE(b.starts_at::date, b.booking_date) > current_date)
              AND NOT EXISTS (
                  SELECT 1 FROM follow_ups f
                  WHERE f.customer_id = c.id AND f.status = 'pending'
                    AND f.due_at::date > current_date)
            ORDER BY c.code
            LIMIT 500
            """, args, cancellationToken: ct))).ToList();

        report.TomorrowBookings = (await connection.QueryAsync<ShiftBookingRow>(new CommandDefinition("""
            SELECT b.id AS Id, b.doc_no AS DocNo, c.name_ar AS CustomerName,
                   b.booking_date AS BookingDate, b.starts_at AS StartsAt, b.status AS Status,
                   b.total_amount AS TotalAmount, e.name_ar AS EmployeeName
            FROM bookings b
            LEFT JOIN customers c ON c.id = b.customer_id
            LEFT JOIN employees e ON e.id = b.assigned_employee_id
            WHERE b.branch_id = @branchId AND b.booking_date = current_date + 1
              AND b.status NOT IN ('cancelled', 'expired')
            ORDER BY b.starts_at NULLS LAST, b.doc_no
            """, args, cancellationToken: ct))).ToList();

        report.TomorrowAppointments = (await connection.QueryAsync<ShiftAppointmentRow>(new CommandDefinition("""
            SELECT a.id AS Id, e.name_ar AS EmployeeName, c.name_ar AS CustomerName,
                   a.starts_at AS StartsAt, a.ends_at AS EndsAt, a.status AS Status
            FROM appointments a
            JOIN employees e ON e.id = a.employee_id
            LEFT JOIN customers c ON c.id = a.customer_id
            WHERE a.branch_id = @branchId AND a.starts_at::date = current_date + 1
              AND a.status <> 'cancelled'
            ORDER BY a.starts_at
            """, args, cancellationToken: ct))).ToList();

        report.ReviewItems = (await QueryReviewItemsAsync(connection, shiftId, ct)).ToList();
        return report;
    }
}

