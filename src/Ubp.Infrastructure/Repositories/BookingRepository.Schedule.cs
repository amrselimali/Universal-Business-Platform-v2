using Dapper;
using Npgsql;
using Ubp.Core.Models;
using Ubp.Core.Results;
using Ubp.Infrastructure.Data;

namespace Ubp.Infrastructure.Repositories;

public sealed partial class BookingRepository
{
    /// <summary>
    /// الحجز اليدوي يتطلب موظفاً منفذاً له موعد في شاشة المواعيد يغطي فترة الحجز بالكامل.
    /// </summary>
    private static async Task EnsureScheduledEmployeeAsync(NpgsqlConnection connection,
        NpgsqlTransaction tx, BookingInput input, CancellationToken ct)
    {
        // الحجز بلا وقت جلسة (مثل بيع POS السريع) لا يخضع لجدول المواعيد.
        if (input.StartsAt is null || input.EndsAt is null) return;

        if (input.AssignedEmployeeId is null)
            throw new ValidationAppException("validation.booking_employee_required");

        var employee = await connection.QuerySingleOrDefaultAsync<(bool IsActive,
            bool IsExecutor)>(new CommandDefinition("""
            SELECT is_active AS IsActive, is_service_executor AS IsExecutor
            FROM employees WHERE id = @id
            """, new { id = input.AssignedEmployeeId.Value }, tx, cancellationToken: ct));
        if (!employee.IsActive || !employee.IsExecutor)
            throw new ValidationAppException("validation.booking_employee_invalid");

        var startsAt = DbTime.Utc(input.StartsAt);
        var endsAt = DbTime.Utc(input.EndsAt);
        var covered = await connection.ExecuteScalarAsync<bool>(new CommandDefinition("""
            SELECT EXISTS (
                SELECT 1 FROM appointments a
                WHERE a.employee_id = @employeeId
                  AND a.status <> 'cancelled'
                  AND a.starts_at <= @startsAt
                  AND a.ends_at >= @endsAt
            )
            """, new { employeeId = input.AssignedEmployeeId.Value, startsAt, endsAt }, tx,
            cancellationToken: ct));
        if (!covered)
            throw new ValidationAppException("validation.booking_outside_employee_schedule");
    }
}