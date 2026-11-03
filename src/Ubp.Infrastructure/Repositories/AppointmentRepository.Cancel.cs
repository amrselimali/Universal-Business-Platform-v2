using Dapper;
using Npgsql;

namespace Ubp.Infrastructure.Repositories;

public sealed partial class AppointmentRepository
{
    /// <summary>
    /// إلغاء دوام موظف: كل حجوزات مواعيده غير المنفذة تعود مسودة ويُفرَّغ الموظف المسؤول
    /// وتُحرَّر الكميات المحجوزة، حتى يُعاد جدولتها على دوام آخر.
    /// </summary>
    private static async Task<int> RevertEmployeeBookingsAsync(NpgsqlConnection connection,
        NpgsqlTransaction tx, Guid employeeId, DateTimeOffset startsAt, DateTimeOffset endsAt,
        Guid userId, CancellationToken ct)
    {
        var ids = (await connection.QueryAsync<Guid>(new CommandDefinition("""
            SELECT b.id
            FROM bookings b
            WHERE b.assigned_employee_id = @employeeId
              AND b.status IN ('draft', 'confirmed')
              AND b.starts_at IS NOT NULL AND b.ends_at IS NOT NULL
              AND b.starts_at < @endsAt AND b.ends_at > @startsAt
              AND NOT EXISTS (SELECT 1 FROM booking_lines l
                              WHERE l.booking_id = b.id AND l.delivered_qty > 0)
            """, new { employeeId, startsAt, endsAt }, tx, cancellationToken: ct))).ToList();
        if (ids.Count == 0) return 0;

        var idArray = ids.ToArray();
        await connection.ExecuteAsync(new CommandDefinition("""
            UPDATE stock_balances sb
            SET reserved_qty = GREATEST(sb.reserved_qty - x.qty, 0), updated_at = now()
            FROM (
                SELECT l.item_id, l.warehouse_id, SUM(l.reserved_qty) AS qty
                FROM booking_lines l
                WHERE l.booking_id = ANY(@ids)
                GROUP BY l.item_id, l.warehouse_id
            ) x
            WHERE sb.item_id = x.item_id AND sb.warehouse_id = x.warehouse_id
            """, new { ids = idArray }, tx, cancellationToken: ct));

        await connection.ExecuteAsync(new CommandDefinition(
            "UPDATE booking_lines SET reserved_qty = 0 WHERE booking_id = ANY(@ids)",
            new { ids = idArray }, tx, cancellationToken: ct));

        return await connection.ExecuteAsync(new CommandDefinition("""
            UPDATE bookings
            SET status = 'draft', assigned_employee_id = NULL, updated_by = @userId
            WHERE id = ANY(@ids)
            """, new { ids = idArray, userId }, tx, cancellationToken: ct));
    }
}