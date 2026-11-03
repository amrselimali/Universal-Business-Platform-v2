using Dapper;
using Ubp.Core.Models;
using Ubp.Core.Results;
using Ubp.Infrastructure.Data;

namespace Ubp.Infrastructure.Repositories;

public interface IBookingExecutionRepository
{
    Task<BookingExecutionPreview> GetPreviewAsync(Guid bookingLineId,
        CancellationToken ct = default);
    Task<BookingExecutionResult> ExecuteAsync(BookingExecutionInput input, Guid userId,
        bool allowNegative, CancellationToken ct = default);
}

public sealed partial class BookingExecutionRepository : IBookingExecutionRepository
{
    private readonly IDbConnectionFactory _factory;
    public BookingExecutionRepository(IDbConnectionFactory factory) => _factory = factory;

    public async Task<BookingExecutionPreview> GetPreviewAsync(Guid bookingLineId,
        CancellationToken ct = default)
    {
        await using var connection = await _factory.OpenAsync(ct);
        var preview = await connection.QuerySingleOrDefaultAsync<BookingExecutionPreview>(
            new CommandDefinition("""
                SELECT b.id AS BookingId, l.id AS BookingLineId, b.doc_no AS BookingNo,
                       b.branch_id AS BranchId, b.customer_id AS CustomerId,
                       c.name_ar AS CustomerName, l.item_id AS ItemId, i.code AS ItemCode,
                       i.name_ar AS ItemName, i.item_type AS ItemType, l.unit_id AS UnitId,
                       u.name_ar AS UnitName, l.warehouse_id AS WarehouseId,
                       w.name_ar AS WarehouseName, l.quantity AS Quantity,
                       l.delivered_qty AS DeliveredQty, l.cancelled_qty AS CancelledQty,
                       l.reserved_qty AS ReservedQty,
                       GREATEST(l.quantity - l.delivered_qty - l.cancelled_qty, 0) AS RemainingQty,
                       l.unit_price AS UnitPrice,
                       COALESCE(resp.employee_name, ap.employee_name) AS EmployeeName,
                       i.is_metered AS ItemIsMetered
                FROM booking_lines l
                JOIN bookings b ON b.id = l.booking_id
                JOIN customers c ON c.id = b.customer_id
                JOIN items i ON i.id = l.item_id
                JOIN units u ON u.id = l.unit_id
                JOIN warehouses w ON w.id = l.warehouse_id
                LEFT JOIN (SELECT time_zone FROM app_settings LIMIT 1) st ON true
                -- (1) المصدر الأساسي: الموظف المسؤول المسجل على الحجز، نشط ومنفذ خدمة،
                -- وله دوام (موعد) يغطي فترة الجلسة. هذا هو الربط الصحيح بين حجز الموعد والتنفيذ.
                LEFT JOIN LATERAL (
                    SELECT emp.name_ar AS employee_name
                    FROM employees emp
                    WHERE emp.id = b.assigned_employee_id
                      AND emp.is_active
                      AND emp.is_service_executor
                      AND (b.starts_at IS NULL OR b.ends_at IS NULL OR EXISTS (
                            SELECT 1 FROM appointments sh
                            WHERE sh.employee_id = emp.id
                              AND sh.status <> 'cancelled'
                              AND sh.starts_at <= b.starts_at
                              AND sh.ends_at >= b.ends_at))
                    LIMIT 1
                ) resp ON true
                -- (2) توافق خلفي: موعد مؤكد للعميل (بيانات أُنشئت قبل شاشة دوامات الموظفين).
                LEFT JOIN LATERAL (
                    SELECT emp.name_ar AS employee_name
                    FROM appointments ap
                    JOIN employees emp ON emp.id = ap.employee_id
                    WHERE ap.status = 'confirmed'
                      AND ap.customer_id = b.customer_id
                      AND ap.branch_id = b.branch_id
                    ORDER BY CASE WHEN ap.booking_id = b.id THEN 0 ELSE 1 END,
                             CASE WHEN (ap.starts_at AT TIME ZONE COALESCE(st.time_zone, 'UTC'))::date =
                                       (now() AT TIME ZONE COALESCE(st.time_zone, 'UTC'))::date
                                  THEN 0 ELSE 1 END,
                             ap.starts_at DESC
                    LIMIT 1
                ) ap ON true
                WHERE l.id = @bookingLineId
                """, new { bookingLineId }, cancellationToken: ct))
            ?? throw new NotFoundAppException();

        if (preview.ItemIsMetered)
        {
            preview.MeterAssets = (await connection.QueryAsync<ExecutionMeterAsset>(
                new CommandDefinition("""
                    SELECT id AS Id, code AS Code, name_ar AS NameAr, meter_type AS MeterType,
                           current_meter_value AS CurrentMeterValue
                    FROM fixed_assets
                    WHERE is_metered AND status = 'ACTIVE'
                      AND (branch_id IS NULL OR branch_id = @branchId)
                    ORDER BY name_ar
                    """, new { branchId = preview.BranchId }, cancellationToken: ct))).ToList();
        }

        return preview;
    }
}
