using Dapper;
using Npgsql;
using Ubp.Core.Models;
using Ubp.Core.Results;
using Ubp.Infrastructure.Data;

namespace Ubp.Infrastructure.Repositories;

public interface IFollowUpRepository
{
    Task<Guid> CreateAsync(Guid id, FollowUpInput input, Guid userId,
        CancellationToken ct = default);
    Task<FollowUp?> GetAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<FollowUp>> ListAsync(string? status, int limit,
        CancellationToken ct = default);
    Task<FollowUp> CompleteAsync(Guid id, int version, Guid userId,
        CancellationToken ct = default);
    Task<int> CreateOverdueBookingFollowUpsAsync(Guid userId, CancellationToken ct = default);
}

/// <summary>متابعات العملاء (4H): تُنشأ يدوياً أو تلقائياً للحجوزات المتأخرة بلا تكرار.</summary>
public sealed class FollowUpRepository : IFollowUpRepository
{
    private readonly IDbConnectionFactory _factory;
    public FollowUpRepository(IDbConnectionFactory factory) => _factory = factory;

    private const string Select = """
        SELECT f.id AS Id, f.customer_id AS CustomerId, c.name_ar AS CustomerName,
               f.booking_id AS BookingId, b.doc_no AS BookingNo,
               f.appointment_id AS AppointmentId, f.assigned_user_id AS AssignedUserId,
               u.name AS AssignedUserName, f.due_at AS DueAt, f.channel AS Channel,
               f.note AS Note, f.status AS Status, f.completed_at AS CompletedAt,
               f.version AS Version
        FROM follow_ups f
        JOIN customers c ON c.id = f.customer_id
        LEFT JOIN bookings b ON b.id = f.booking_id
        LEFT JOIN users u ON u.id = f.assigned_user_id
        """;

    public async Task<Guid> CreateAsync(Guid id, FollowUpInput input, Guid userId,
        CancellationToken ct = default)
    {
        if (input.CustomerId == Guid.Empty)
            throw new ValidationAppException("validation.required_fields");
        if (!FollowUpChannels.IsValid(input.Channel))
            throw new ValidationAppException("validation.followup_channel");

        await using var connection = await _factory.OpenAsync(ct);
        try
        {
            await connection.ExecuteAsync(new CommandDefinition("""
                INSERT INTO follow_ups (id, customer_id, booking_id, appointment_id,
                    assigned_user_id, due_at, channel, note, status, created_by)
                VALUES (@id, @CustomerId, @BookingId, @AppointmentId, @AssignedUserId, @DueAt,
                        @Channel, @Note, 'pending', @userId)
                """, new { id, input.CustomerId, input.BookingId, input.AppointmentId,
                    input.AssignedUserId,
                    DueAt = Ubp.Infrastructure.Data.DbTime.Utc(input.DueAt), input.Channel,
                    input.Note, userId },
                cancellationToken: ct));
        }
        catch (PostgresException ex) when (ex.ConstraintName == "ux_followups_pending_booking")
        {
            throw new ValidationAppException("validation.followup_exists");
        }
        return id;
    }

    public async Task<FollowUp?> GetAsync(Guid id, CancellationToken ct = default)
    {
        await using var connection = await _factory.OpenAsync(ct);
        return await connection.QuerySingleOrDefaultAsync<FollowUp>(new CommandDefinition(
            Select + " WHERE f.id = @id", new { id }, cancellationToken: ct));
    }

    public async Task<IReadOnlyList<FollowUp>> ListAsync(string? status, int limit,
        CancellationToken ct = default)
    {
        await using var connection = await _factory.OpenAsync(ct);
        return (await connection.QueryAsync<FollowUp>(new CommandDefinition(Select + """
             WHERE (@status::varchar IS NULL OR f.status = @status::varchar)
             ORDER BY f.status = 'pending' DESC, f.due_at
             LIMIT @limit
            """, new { status, limit = Math.Clamp(limit, 1, 500) }, cancellationToken: ct)))
            .ToList();
    }

    public async Task<FollowUp> CompleteAsync(Guid id, int version, Guid userId,
        CancellationToken ct = default)
    {
        await using var connection = await _factory.OpenAsync(ct);
        var updated = await connection.ExecuteAsync(new CommandDefinition("""
            UPDATE follow_ups SET status = 'done', completed_at = now(), updated_by = @userId
            WHERE id = @id AND version = @version AND status = 'pending'
            """, new { id, version, userId }, cancellationToken: ct));
        if (updated == 0) throw new ConflictException();
        return (await GetAsync(id, ct))!;
    }

    /// <summary>يولّد متابعة لكل حجز مؤكد تجاوز تاريخ استحقاقه بلا متابعة معلّقة.</summary>
    public async Task<int> CreateOverdueBookingFollowUpsAsync(Guid userId,
        CancellationToken ct = default)
    {
        await using var connection = await _factory.OpenAsync(ct);
        return await connection.ExecuteAsync(new CommandDefinition("""
            INSERT INTO follow_ups (id, customer_id, booking_id, assigned_user_id, due_at,
                channel, note, status, created_by)
            SELECT gen_random_uuid(), b.customer_id, b.id, u.id, now(),
                   'call', 'متابعة حجز متأخر ' || COALESCE(b.doc_no, ''), 'pending', @userId
            FROM bookings b
            JOIN LATERAL (SELECT id FROM users WHERE is_active ORDER BY created_at LIMIT 1) u ON true
            WHERE b.status IN ('confirmed', 'partially_delivered')
              AND COALESCE(b.due_date, b.booking_date) < current_date
              AND NOT EXISTS (SELECT 1 FROM follow_ups f
                              WHERE f.booking_id = b.id AND f.status = 'pending')
            ON CONFLICT (booking_id) WHERE status = 'pending' AND booking_id IS NOT NULL
            DO NOTHING
            """, new { userId }, cancellationToken: ct));
    }
}
