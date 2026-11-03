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

    /// <summary>
    /// يثبّت الموظف المسؤول على الحجز قبل التحقق من الجدول:
    /// إن لم يكن مسجلاً على الحجز يُستنتج من موعد العميل (المرتبط بالحجز أولاً، ثم المؤكد،
    /// ثم نفس الفرع/اليوم)، ثم يُسجَّل الإسناد الافتراضي بدور owner إن لم يكن مفتوحاً.
    /// </summary>
    private static async Task EnsureResponsibleEmployeeAsync(NpgsqlConnection connection,
        NpgsqlTransaction tx, Guid bookingId, Guid userId, CancellationToken ct)
    {
        var head = await connection.QuerySingleOrDefaultAsync<(Guid? EmployeeId, Guid CustomerId,
            Guid BranchId, DateTimeOffset? StartsAt, DateTime BookingDate)>(
            new CommandDefinition("""
            SELECT assigned_employee_id AS EmployeeId, customer_id AS CustomerId,
                   branch_id AS BranchId, starts_at AS StartsAt, booking_date AS BookingDate
            FROM bookings WHERE id = @id
            """, new { id = bookingId }, tx, cancellationToken: ct));

        var employeeId = head.EmployeeId;
        if (employeeId is null)
        {
            var sessionDate = DbTime.Utc(head.StartsAt)?.UtcDateTime.Date;
            employeeId = await connection.QuerySingleOrDefaultAsync<Guid?>(new CommandDefinition("""
                SELECT a.employee_id
                FROM appointments a
                JOIN employees e ON e.id = a.employee_id
                WHERE a.customer_id = @customerId
                  AND a.status <> 'cancelled'
                ORDER BY CASE WHEN a.booking_id = @bookingId THEN 0 ELSE 1 END,
                         CASE WHEN a.status = 'confirmed' THEN 0 ELSE 1 END,
                         CASE WHEN a.branch_id = @branchId THEN 0 ELSE 1 END,
                         CASE WHEN a.starts_at::date =
                                   COALESCE(@sessionDate::date, @bookingDate) THEN 0 ELSE 1 END,
                         a.starts_at DESC
                LIMIT 1
                """, new
            {
                customerId = head.CustomerId, bookingId, branchId = head.BranchId,
                sessionDate, bookingDate = head.BookingDate
            }, tx, cancellationToken: ct));

            if (employeeId is null) return;
            await connection.ExecuteAsync(new CommandDefinition(
                "UPDATE bookings SET assigned_employee_id = @employeeId WHERE id = @id",
                new { employeeId, id = bookingId }, tx, cancellationToken: ct));
        }

        var hasOpen = await connection.ExecuteScalarAsync<bool>(new CommandDefinition("""
            SELECT EXISTS (
                SELECT 1 FROM booking_assignments
                WHERE booking_id = @id AND role = 'owner' AND unassigned_at IS NULL
                  AND employee_id = @employeeId)
            """, new { id = bookingId, employeeId }, tx, cancellationToken: ct));
        if (hasOpen) return;

        await connection.ExecuteAsync(new CommandDefinition("""
            UPDATE booking_assignments SET unassigned_at = now()
            WHERE booking_id = @id AND role = 'owner' AND unassigned_at IS NULL
            """, new { id = bookingId }, tx, cancellationToken: ct));
        await connection.ExecuteAsync(new CommandDefinition("""
            INSERT INTO booking_assignments (booking_id, employee_id, role, assigned_by)
            VALUES (@id, @employeeId, 'owner', @userId)
            """, new { id = bookingId, employeeId, userId }, tx, cancellationToken: ct));
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