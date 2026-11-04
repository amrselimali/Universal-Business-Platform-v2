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
    Task<IReadOnlyList<FollowUp>> ListAsync(string? status, DateTime? from, DateTime? to,
        string? customerSearch, int limit, CancellationToken ct = default);
    Task<IReadOnlyList<CustomerGapRow>> FindIdleCustomersAsync(string? codeFrom, string? codeTo,
        Guid? branchId, IReadOnlyCollection<Guid> branchIds, int limit,
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

    public async Task<IReadOnlyList<FollowUp>> ListAsync(string? status, DateTime? from,
        DateTime? to, string? customerSearch, int limit, CancellationToken ct = default)
    {
        await using var connection = await _factory.OpenAsync(ct);
        return (await connection.QueryAsync<FollowUp>(new CommandDefinition(Select + """
             WHERE (@status::varchar IS NULL OR f.status = @status::varchar)
               AND (@from::date IS NULL OR f.due_at::date >= @from::date)
               AND (@to::date IS NULL OR f.due_at::date <= @to::date)
               AND (@customerSearch::text IS NULL OR @customerSearch = ''
                    OR c.search_text LIKE '%' || normalize_search(@customerSearch) || '%')
             ORDER BY f.status = 'pending' DESC, f.due_at
             LIMIT @limit
            """, new
        {
            status, from = from?.Date, to = to?.Date, customerSearch,
            limit = Math.Clamp(limit, 1, 500)
        }, cancellationToken: ct))).ToList();
    }

    /// <summary>
    /// عملاء نشطون بلا حجز ولا موعد ولا متابعة **من اليوم إلى ما لا نهاية**، مع آخر نشاط مسجّل.
    /// أكواد العملاء تُفلتر بنطاق (من كود → إلى كود)، ويُطبَّق فلتر الفرع ونطاق فروع المستخدم.
    /// </summary>
    public async Task<IReadOnlyList<CustomerGapRow>> FindIdleCustomersAsync(string? codeFrom,
        string? codeTo, Guid? branchId, IReadOnlyCollection<Guid> branchIds, int limit,
        CancellationToken ct = default)
    {
        await using var connection = await _factory.OpenAsync(ct);
        var rows = await connection.QueryAsync<CustomerGapRow>(new CommandDefinition("""
            SELECT c.id AS CustomerId, c.code AS Code, c.name_ar AS NameAr, c.phone AS Phone,
                   br.name_ar AS BranchName,
                   lb.last_at AS LastBookingAt, lb.doc_no AS LastBookingNo,
                   lb.status AS LastBookingStatus, la.last_at AS LastAppointmentAt,
                   lf.last_at AS LastFollowUpAt, lf.note AS LastFollowUpNote,
                   lf.channel AS LastFollowUpChannel, lf.status AS LastFollowUpStatus
            FROM customers c
            LEFT JOIN branches br ON br.id = c.branch_id
            LEFT JOIN LATERAL (
                SELECT COALESCE(b.starts_at, b.booking_date::timestamptz) AS last_at,
                       b.doc_no, b.status
                FROM bookings b
                WHERE b.customer_id = c.id AND b.status NOT IN ('cancelled', 'reversed')
                ORDER BY COALESCE(b.starts_at, b.booking_date::timestamptz) DESC
                LIMIT 1
            ) lb ON true
            LEFT JOIN LATERAL (
                SELECT a.starts_at AS last_at
                FROM appointments a
                WHERE a.customer_id = c.id AND a.status <> 'cancelled'
                ORDER BY a.starts_at DESC
                LIMIT 1
            ) la ON true
            LEFT JOIN LATERAL (
                SELECT f.due_at AS last_at, f.note, f.channel, f.status
                FROM follow_ups f
                WHERE f.customer_id = c.id AND f.status = 'pending'
                ORDER BY f.due_at DESC
                LIMIT 1
            ) lf ON true
            WHERE c.is_active
              AND (@branchId::uuid IS NULL OR c.branch_id = @branchId::uuid)
              AND (@allBranches OR c.branch_id IS NULL
                   OR c.branch_id = ANY(@branchIds))
              AND (@codeFrom::text IS NULL OR upper(c.code) >= upper(@codeFrom::text))
              AND (@codeTo::text IS NULL OR upper(c.code) <= upper(@codeTo::text))
              AND NOT EXISTS (
                  SELECT 1 FROM bookings b
                  WHERE b.customer_id = c.id AND b.status NOT IN ('cancelled', 'reversed')
                    AND COALESCE(b.starts_at::date, b.booking_date) >= current_date)
              AND NOT EXISTS (
                  SELECT 1 FROM appointments a
                  WHERE a.customer_id = c.id AND a.status <> 'cancelled'
                    AND a.starts_at::date >= current_date)
              AND NOT EXISTS (
                  SELECT 1 FROM follow_ups f
                  WHERE f.customer_id = c.id AND f.status = 'pending'
                    AND f.due_at::date >= current_date)
            ORDER BY COALESCE(lf.last_at, lb.last_at, la.last_at) ASC NULLS FIRST, c.name_ar
            LIMIT @take
            """, new
        {
            codeFrom = string.IsNullOrWhiteSpace(codeFrom) ? null : codeFrom.Trim(),
            codeTo = string.IsNullOrWhiteSpace(codeTo) ? null : codeTo.Trim(),
            branchId, allBranches = branchIds.Count == 0, branchIds = branchIds.ToArray(),
            take = Math.Clamp(limit, 1, 1000)
        }, cancellationToken: ct));
        return rows.ToList();
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
