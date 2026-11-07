using Dapper;
using Npgsql;
using Ubp.Core.Results;

namespace Ubp.Infrastructure.Repositories;

public sealed partial class BookingRepository
{
    /// <summary>
    /// يمنع أكثر من حجز (=موعد) لنفس العميل مع نفس الموظف المسؤول في نفس اليوم.
    /// يُستثنى الحجز الجاري تعديله (excludeBookingId)، وتُستثنى الحجوزات الملغاة.
    /// </summary>
    private static async Task EnsureNoDuplicateDailyBookingAsync(NpgsqlConnection connection,
        NpgsqlTransaction tx, Guid customerId, Guid employeeId, DateTime bookingDate,
        Guid? excludeBookingId, CancellationToken ct)
    {
        var exists = await connection.ExecuteScalarAsync<bool>(new CommandDefinition("""
            SELECT EXISTS (
                SELECT 1 FROM bookings
                WHERE customer_id = @customerId AND assigned_employee_id = @employeeId
                  AND booking_date = @bookingDate
                  AND status NOT IN ('cancelled', 'reversed')
                  AND (@excludeId::uuid IS NULL OR id <> @excludeId)
            )
            """, new { customerId, employeeId, bookingDate = bookingDate.Date,
                excludeId = excludeBookingId }, tx, cancellationToken: ct));
        if (exists)
            throw new ValidationAppException("validation.booking_duplicate_same_day");
    }
}
