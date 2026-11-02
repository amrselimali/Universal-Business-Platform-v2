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
        return await connection.QuerySingleOrDefaultAsync<BookingExecutionPreview>(
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
                       COALESCE(ap.employee_name, e.name_ar) AS EmployeeName,
                       i.metered_asset_id AS MeteredAssetId, fa.name_ar AS MeteredAssetName,
                       fa.meter_type AS MeterType,
                       COALESCE(fa.current_meter_value, 0) AS CurrentMeterValue
                FROM booking_lines l
                JOIN bookings b ON b.id = l.booking_id
                JOIN customers c ON c.id = b.customer_id
                JOIN items i ON i.id = l.item_id
                JOIN units u ON u.id = l.unit_id
                JOIN warehouses w ON w.id = l.warehouse_id
                LEFT JOIN employees e ON e.id = b.assigned_employee_id
                LEFT JOIN fixed_assets fa ON fa.id = i.metered_asset_id
                LEFT JOIN LATERAL (
                    SELECT ap.employee_id, emp.name_ar AS employee_name
                    FROM appointments ap
                    JOIN employees emp ON emp.id = ap.employee_id
                    WHERE ap.customer_id = b.customer_id
                      AND ap.status <> 'cancelled'
                      AND ap.starts_at::date = COALESCE(b.starts_at::date, b.booking_date)
                    ORDER BY ap.starts_at DESC
                    LIMIT 1
                ) ap ON true
                WHERE l.id = @bookingLineId
                """, new { bookingLineId }, cancellationToken: ct))
            ?? throw new NotFoundAppException();
    }
}
