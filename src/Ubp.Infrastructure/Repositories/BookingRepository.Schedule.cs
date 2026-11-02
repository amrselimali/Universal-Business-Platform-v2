using Dapper;
using Npgsql;
using Ubp.Core.Models;
using Ubp.Core.Results;
using Ubp.Infrastructure.Data;

namespace Ubp.Infrastructure.Repositories;

public sealed partial class BookingRepository
{
    /// <summary>
    /// حفظ المسودة: الموظف اختياري، وإن وُجد فيجب أن يكون نشطاً ومفعّلاً كمنفذ.
    /// لا يُشترط وجود موعد له في شاشة المواعيد أثناء المسودة.
    /// </summary>
    private static async Task EnsureDraftEmployeeAsync(NpgsqlConnection connection,
        NpgsqlTransaction tx, Guid? employeeId, CancellationToken ct)
    {
        if (employeeId is null || employeeId == Guid.Empty) return;

        var active = await connection.ExecuteScalarAsync<bool>(new CommandDefinition(
            "SELECT is_active FROM employees WHERE id = @id",
            new { id = employeeId.Value }, tx, cancellationToken: ct));
        if (!active) throw new ValidationAppException("validation.booking_employee_invalid");
    }

    /// <summary>
    /// تأكيد الحجز: يتطلب موظفاً منفذاً له موعد في شاشة المواعيد يغطي فترة الحجز بالكامل.
    /// </summary>
    private static async Task EnsureScheduledEmployeeAsync(NpgsqlConnection connection,
        NpgsqlTransaction tx, Guid bookingId, CancellationToken ct)
    {
        var head = await connection.QuerySingleOrDefaultAsync<(Guid? EmployeeId,
            DateTimeOffset? StartsAt, DateTimeOffset? EndsAt, DateTime? BookingDate)>(
            new CommandDefinition("""
            SELECT assigned_employee_id AS EmployeeId, starts_at AS StartsAt,
                   ends_at AS EndsAt, booking_date AS BookingDate
            FROM bookings WHERE id = @id
            """, new { id = bookingId }, tx, cancellationToken: ct));

        // الحجز بلا وقت جلسة (مثل بيع POS السريع) لا يخضع لجدول المواعيد.
        if (head.StartsAt is null || head.EndsAt is null) return;

        if (head.EmployeeId is null)
            throw new ValidationAppException("validation.booking_employee_required");

        await EnsureExecutorEmployeeAsync(connection, tx, head.EmployeeId.Value, ct);

        var covered = await connection.ExecuteScalarAsync<bool>(new CommandDefinition("""
            SELECT EXISTS (
                SELECT 1 FROM appointments a
                WHERE a.employee_id = @employeeId
                  AND a.status <> 'cancelled'
                  AND a.starts_at <= @startsAt
                  AND a.ends_at >= @endsAt
            )
            """, new
        {
            employeeId = head.EmployeeId.Value,
            startsAt = DbTime.Utc(head.StartsAt.Value),
            endsAt = DbTime.Utc(head.EndsAt.Value)
        }, tx, cancellationToken: ct));
        if (!covered)
            throw new ValidationAppException("validation.booking_outside_employee_schedule");
    }

    private static async Task EnsureExecutorEmployeeAsync(NpgsqlConnection connection,
        NpgsqlTransaction tx, Guid employeeId, CancellationToken ct)
    {
        var employee = await connection.QuerySingleOrDefaultAsync<(bool IsActive,
            bool IsExecutor)>(new CommandDefinition("""
            SELECT is_active AS IsActive, is_service_executor AS IsExecutor
            FROM employees WHERE id = @id
            """, new { id = employeeId }, tx, cancellationToken: ct));
        if (!employee.IsActive || !employee.IsExecutor)
            throw new ValidationAppException("validation.booking_employee_invalid");
    }
}