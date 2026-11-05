using Dapper;
using Ubp.Core.Models;
using Ubp.Core.Results;

namespace Ubp.Infrastructure.Repositories;

public sealed partial class TreasuryRepository
{
    /// <summary>
    /// تقرير الوردية (0077): جدول واحد للتحصيلات والإيرادات مرتب بالعميل، عدّ نقدية كل خزينة
    /// (بالمتوقع الحالي للوردية المفتوحة)، حركة عدادات الأجهزة، والملاحظات — ثم عملاء بلا
    /// متابعة وحجوزات/مواعيد الغد في نهاية التقرير.
    /// </summary>
    public async Task<ShiftReport> GetShiftReportAsync(Guid shiftId, CancellationToken ct = default)
    {
        var shift = await GetShiftAsync(shiftId, ct) ?? throw new NotFoundAppException();
        await using var connection = await _factory.OpenAsync(ct);
        var args = new
        {
            branchId = shift.BranchId, openedAt = shift.OpenedAt, until = shift.ClosedAt
        };

        var report = new ShiftReport { Shift = shift, Notes = shift.Notes };

        report.Lines = (await connection.QueryAsync<ShiftCollectionRow>(new CommandDefinition("""
            WITH receipts AS (
                SELECT v.party_name AS CustomerName, t.treasury_type AS TreasuryType,
                       v.total_amount AS Collected,
                       0::numeric AS Revenue, 0::numeric AS Quantity, 0::numeric AS MeterUnits,
                       NULL::text AS EmployeeName
                FROM vouchers v
                JOIN treasuries t ON t.id = v.treasury_id
                WHERE v.branch_id = @branchId AND v.voucher_type = 'receipt'
                  AND v.status <> 'cancelled' AND v.created_at >= @openedAt
                  AND (@until::timestamptz IS NULL OR v.created_at <= @until::timestamptz)
            ), revenues AS (
                SELECT COALESCE(c.name_ar, d.party_name) AS CustomerName, NULL::varchar AS TreasuryType,
                       0::numeric AS Collected,
                       d.total AS Revenue, 0::numeric AS Quantity, 0::numeric AS MeterUnits,
                       NULL::text AS EmployeeName
                FROM commercial_documents d
                LEFT JOIN customers c ON c.id = d.customer_id
                WHERE d.branch_id = @branchId AND d.doc_type = 'sales_invoice'
                  AND d.status <> 'cancelled' AND d.created_at >= @openedAt
                  AND (@until::timestamptz IS NULL OR d.created_at <= @until::timestamptz)
            ), executions AS (
                SELECT c.name_ar AS CustomerName, NULL::varchar AS TreasuryType,
                       0::numeric AS Collected, 0::numeric AS Revenue,
                       SUM(el.quantity) AS Quantity, SUM(el.meter_units) AS MeterUnits,
                       MAX(e.name_ar) AS EmployeeName
                FROM booking_executions be
                JOIN booking_execution_lines el ON el.execution_id = be.id
                JOIN bookings b ON b.id = be.booking_id
                JOIN customers c ON c.id = b.customer_id
                LEFT JOIN employees e ON e.id = b.assigned_employee_id
                WHERE be.branch_id = @branchId AND be.status = 'posted'
                  AND be.created_at >= @openedAt
                  AND (@until::timestamptz IS NULL OR be.created_at <= @until::timestamptz)
                GROUP BY c.name_ar
            )
            SELECT CustomerName, MAX(TreasuryType) AS TreasuryType,
                   SUM(Collected) AS Collected, SUM(Revenue) AS Revenue,
                   SUM(Quantity) AS Quantity, SUM(MeterUnits) AS MeterUnits,
                   MAX(EmployeeName) AS EmployeeName
            FROM (SELECT * FROM receipts
                  UNION ALL SELECT * FROM revenues
                  UNION ALL SELECT * FROM executions) x
            GROUP BY CustomerName
            ORDER BY CustomerName
            """, args, cancellationToken: ct))).ToList();

        // ضمان ظهور كل خزائن الفرع المتابَعة حتى للورديات المفتوحة قبل الترحيل 0076.
        await connection.ExecuteAsync(new CommandDefinition("""
            INSERT INTO shift_cash_counts (id, shift_id, treasury_id, opening_cash, expected_cash, status)
            SELECT gen_random_uuid(), @shiftId, t.id, 0, 0,
                   CASE WHEN @isOpen THEN 'open' ELSE 'draft' END
            FROM treasuries t
            WHERE t.branch_id = @branchId AND t.is_active
              AND EXISTS (SELECT 1 FROM treasury_methods m
                          WHERE m.treasury_id = t.id AND m.is_active AND m.follow_in_shift)
            ON CONFLICT (shift_id, treasury_id) DO NOTHING
            """, new { shiftId, branchId = shift.BranchId, isOpen = shift.IsOpen },
            cancellationToken: ct));

        report.CashCounts = (await QueryCashCountsAsync(connection, shiftId, null, ct)).ToList();
        if (shift.IsOpen)
        {
            decimal expected = 0;
            foreach (var count in report.CashCounts)
            {
                var movement = await TreasuryMovementAsync(connection, null, count.TreasuryId,
                    shift.OpenedAt, ct);
                // النقد المتوقع = المحصل − المنصرف للخزينة خلال الوردية (0078).
                count.ExpectedCash = Math.Round(movement, 2, MidpointRounding.AwayFromZero);
                expected += count.ExpectedCash;
            }
            shift.ExpectedCash = Math.Round(expected, 2, MidpointRounding.AwayFromZero);
        }

        report.Meters = (await connection.QueryAsync<ShiftMeterMovementRow>(new CommandDefinition("""
            SELECT fa.id AS AssetId, fa.code AS Code, fa.name_ar AS NameAr,
                   COALESCE((SELECT r.value FROM asset_meter_readings r
                             WHERE r.asset_id = fa.id AND r.reading_at < @openedAt
                             ORDER BY r.reading_at DESC LIMIT 1),
                            fa.current_meter_value - COALESCE(cons.consumed, 0)) AS Opening,
                   COALESCE(cons.consumed, 0) AS Consumed,
                   COALESCE((SELECT r.value FROM asset_meter_readings r
                             WHERE r.asset_id = fa.id AND r.reading_at < @openedAt
                             ORDER BY r.reading_at DESC LIMIT 1),
                            fa.current_meter_value - COALESCE(cons.consumed, 0))
                       + COALESCE(cons.consumed, 0) AS Closing,
                   fa.current_meter_value AS Actual
            FROM fixed_assets fa
            LEFT JOIN LATERAL (
                SELECT SUM(el.meter_units) AS consumed
                FROM booking_execution_lines el
                JOIN booking_executions be ON be.id = el.execution_id
                WHERE el.metered_asset_id = fa.id AND be.status = 'posted'
                  AND be.created_at >= @openedAt
                  AND (@until::timestamptz IS NULL OR be.created_at <= @until::timestamptz)
            ) cons ON true
            WHERE fa.branch_id = @branchId AND fa.status <> 'DISPOSED'
              AND (fa.is_metered OR EXISTS (SELECT 1 FROM asset_meter_readings r
                                            WHERE r.asset_id = fa.id))
            ORDER BY fa.code
            """, args, cancellationToken: ct))).ToList();

        report.Expenses = (await connection.QueryAsync<ShiftExpenseRow>(new CommandDefinition("""
            SELECT t.treasury_type AS TreasuryType, t.name_ar AS TreasuryName,
                   el.amount AS Amount, i.name_ar AS ItemName, el.quantity AS Quantity,
                   el.unit_price AS UnitPrice, COALESCE(el.description, o.notes) AS Description,
                   o.status AS Status
            FROM operating_expenses o
            JOIN operating_expense_lines el ON el.expense_id = o.id
            LEFT JOIN treasuries t ON t.id = o.treasury_id
            LEFT JOIN items i ON i.id = el.item_id
            WHERE o.branch_id = @branchId AND o.created_at >= @openedAt
              AND (@until::timestamptz IS NULL OR o.created_at <= @until::timestamptz)
            ORDER BY o.doc_date, o.doc_no, el.line_no
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

        return report;
    }

    /// <summary>حركة عدادات الأجهزة في الوردية (للجزء المستقل في شاشة الورديات).</summary>
    public async Task<IReadOnlyList<ShiftMeterMovementRow>> ListShiftMetersAsync(Guid shiftId,
        CancellationToken ct = default)
    {
        var report = await GetShiftReportAsync(shiftId, ct);
        return report.Meters;
    }
}

