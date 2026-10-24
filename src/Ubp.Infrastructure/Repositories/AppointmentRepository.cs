using Dapper;
using Npgsql;
using Ubp.Core.Models;
using Ubp.Core.Results;
using Ubp.Infrastructure.Data;

namespace Ubp.Infrastructure.Repositories;

public interface IAppointmentRepository
{
    Task<Guid> CreateAsync(Guid id, AppointmentInput input, Guid userId,
        CancellationToken ct = default);
    Task<Appointment> SetStatusAsync(Guid id, int version, string status, Guid userId,
        CancellationToken ct = default);
    Task<Appointment?> GetAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<Appointment>> ListDayAsync(Guid? branchId, DateTime date,
        CancellationToken ct = default);
}

/// <summary>
/// المواعيد: منع تداخل موظف واحد بقيد EXCLUDE في القاعدة (D10)، وإلغاء الموعد يحرّر الفترة.
/// </summary>
public sealed class AppointmentRepository : IAppointmentRepository
{
    private readonly IDbConnectionFactory _factory;
    public AppointmentRepository(IDbConnectionFactory factory) => _factory = factory;

    private sealed class AppointmentRow
    {
        public Guid Id { get; set; }
        public Guid BranchId { get; set; }
        public Guid EmployeeId { get; set; }
        public string? EmployeeName { get; set; }
        public Guid? BookingId { get; set; }
        public string? BookingNo { get; set; }
        public Guid? CustomerId { get; set; }
        public string? CustomerName { get; set; }
        public DateTimeOffset StartsAt { get; set; }
        public DateTimeOffset EndsAt { get; set; }
        public string Status { get; set; } = AppointmentStatuses.Scheduled;
        public string? Notes { get; set; }
        public int Version { get; set; }
    }

    private const string Select = """
        SELECT a.id AS Id, a.branch_id AS BranchId, a.employee_id AS EmployeeId,
               e.name_ar AS EmployeeName, a.booking_id AS BookingId, b.doc_no AS BookingNo,
               a.customer_id AS CustomerId, c.name_ar AS CustomerName, a.starts_at AS StartsAt,
               a.ends_at AS EndsAt, a.status AS Status, a.notes AS Notes, a.version AS Version
        FROM appointments a
        JOIN employees e ON e.id = a.employee_id
        LEFT JOIN bookings b ON b.id = a.booking_id
        LEFT JOIN customers c ON c.id = a.customer_id
        """;

    public async Task<Guid> CreateAsync(Guid id, AppointmentInput input, Guid userId,
        CancellationToken ct = default)
    {
        if (input.BranchId == Guid.Empty || input.EmployeeId == Guid.Empty)
            throw new ValidationAppException("validation.required_fields");
        if (input.EndsAt <= input.StartsAt)
            throw new ValidationAppException("validation.appointment_range");

        await using var connection = await _factory.OpenAsync(ct);
        try
        {
            await connection.ExecuteAsync(new CommandDefinition("""
                INSERT INTO appointments (id, branch_id, employee_id, booking_id, customer_id,
                    starts_at, ends_at, status, notes, created_by)
                VALUES (@id, @BranchId, @EmployeeId, @BookingId, @CustomerId, @StartsAt, @EndsAt,
                        'scheduled', @Notes, @userId)
                """, new { id, input.BranchId, input.EmployeeId, input.BookingId, input.CustomerId,
                    input.StartsAt, input.EndsAt, input.Notes, userId }, cancellationToken: ct));
        }
        catch (PostgresException ex) when (ex.SqlState == "23P01")
        {
            throw new ValidationAppException("validation.appointment_overlap");
        }

        return id;
    }

    public async Task<Appointment> SetStatusAsync(Guid id, int version, string status,
        Guid userId, CancellationToken ct = default)
    {
        if (!AppointmentStatuses.IsValid(status))
            throw new ValidationAppException("validation.appointment_status");

        await using var connection = await _factory.OpenAsync(ct);
        try
        {
            var updated = await connection.ExecuteAsync(new CommandDefinition("""
                UPDATE appointments SET status = @status, updated_by = @userId
                WHERE id = @id AND version = @version
                """, new { status, userId, id, version }, cancellationToken: ct));
            if (updated == 0) throw new ConflictException();
        }
        catch (PostgresException ex) when (ex.SqlState == "23P01")
        {
            throw new ValidationAppException("validation.appointment_overlap");
        }

        return (await GetAsync(id, ct))!;
    }

    public async Task<Appointment?> GetAsync(Guid id, CancellationToken ct = default)
    {
        await using var connection = await _factory.OpenAsync(ct);
        return await connection.QuerySingleOrDefaultAsync<Appointment>(new CommandDefinition(
            Select + " WHERE a.id = @id", new { id }, cancellationToken: ct));
    }

    public async Task<IReadOnlyList<Appointment>> ListDayAsync(Guid? branchId, DateTime date,
        CancellationToken ct = default)
    {
        await using var connection = await _factory.OpenAsync(ct);
        var from = date.Date;
        var to = from.AddDays(1);
        return (await connection.QueryAsync<Appointment>(new CommandDefinition(
            Select + """
             WHERE a.starts_at >= @from AND a.starts_at < @to
               AND (@branchId::uuid IS NULL OR a.branch_id = @branchId::uuid)
             ORDER BY a.starts_at
            """, new { from, to, branchId }, cancellationToken: ct))).ToList();
    }
}
