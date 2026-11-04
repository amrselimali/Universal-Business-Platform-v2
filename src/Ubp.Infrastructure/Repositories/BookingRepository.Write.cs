using Dapper;
using Npgsql;
using Ubp.Core.Models;
using Ubp.Core.Results;
using Ubp.Infrastructure.Data;

namespace Ubp.Infrastructure.Repositories;

public sealed partial class BookingRepository
{
    public async Task<Guid> CreateDraftAsync(Guid id, BookingInput input, Guid userId,
        CancellationToken ct = default)
    {
        if (input.StartsAt is not null && input.EndsAt is not null &&
            input.EndsAt <= input.StartsAt)
            throw new ValidationAppException("validation.booking_time_range");
        if ((input.StartsAt is null) != (input.EndsAt is null))
            throw new ValidationAppException("validation.booking_time_range");

        var lines = await BuildLinesAsync(input, ct);
        await using var connection = await _factory.OpenAsync(ct);
        await using var tx = await connection.BeginTransactionAsync(ct);
        await EnsureDraftEmployeeAsync(connection, tx, input.AssignedEmployeeId, ct);

        await connection.ExecuteAsync(new CommandDefinition("""
            INSERT INTO bookings (id, branch_id, booking_date, status, customer_id, source, due_date,
                starts_at, ends_at, assigned_employee_id, related_booking_id, expires_at,
                total_amount, notes, created_by)
            VALUES (@id, @BranchId, @BookingDate, 'draft', @CustomerId, 'manual', @DueDate,
                    @StartsAt, @EndsAt, @AssignedEmployeeId, @RelatedBookingId, @ExpiresAt,
                    @total, @Notes, @userId)
            """, new
        {
            id, input.BranchId, bookingDate = input.BookingDate.Date, input.CustomerId,
            input.DueDate, StartsAt = DbTime.Utc(input.StartsAt),
            EndsAt = DbTime.Utc(input.EndsAt), ExpiresAt = DbTime.Utc(input.ExpiresAt),
            input.AssignedEmployeeId, input.RelatedBookingId, total = lines.Sum(l => l.TotalAmount),
            input.Notes, userId
        }, tx, cancellationToken: ct));

        await InsertLinesAsync(connection, tx, id, lines, ct);

        if (input.AssignedEmployeeId is not null)
            await connection.ExecuteAsync(new CommandDefinition("""
                INSERT INTO booking_assignments (booking_id, employee_id, role, assigned_by)
                VALUES (@id, @employeeId, 'owner', @userId)
                """, new { id, employeeId = input.AssignedEmployeeId, userId }, tx,
                cancellationToken: ct));

        await tx.CommitAsync(ct);
        return id;
    }

    /// <summary>موعد جلسة لعميل: مسودة بلا بنود (تاريخ/وقت/موظف مسؤول اختياريًا).</summary>
    public async Task<Guid> CreateSessionDraftAsync(Guid id, BookingInput input, Guid userId,
        CancellationToken ct = default)
    {
        if (input.BranchId == Guid.Empty || input.CustomerId == Guid.Empty)
            throw new ValidationAppException("validation.required_fields");
        if (input.StartsAt is null || input.EndsAt is null || input.EndsAt <= input.StartsAt)
            throw new ValidationAppException("validation.booking_time_range");

        await using var connection = await _factory.OpenAsync(ct);
        await using var tx = await connection.BeginTransactionAsync(ct);
        await EnsureDraftEmployeeAsync(connection, tx, input.AssignedEmployeeId, ct);

        await connection.ExecuteAsync(new CommandDefinition("""
            INSERT INTO bookings (id, branch_id, booking_date, status, customer_id, source, due_date,
                starts_at, ends_at, assigned_employee_id, related_booking_id, expires_at,
                total_amount, notes, created_by)
            VALUES (@id, @BranchId, @BookingDate, 'draft', @CustomerId, 'manual', @BookingDate,
                    @StartsAt, @EndsAt, @AssignedEmployeeId, NULL, @ExpiresAt, 0, @Notes, @userId)
            """, new
        {
            id, input.BranchId, bookingDate = input.BookingDate.Date, input.CustomerId,
            StartsAt = DbTime.Utc(input.StartsAt), EndsAt = DbTime.Utc(input.EndsAt),
            ExpiresAt = DbTime.Utc(input.ExpiresAt), input.AssignedEmployeeId, input.Notes, userId
        }, tx, cancellationToken: ct));

        if (input.AssignedEmployeeId is not null)
            await connection.ExecuteAsync(new CommandDefinition("""
                INSERT INTO booking_assignments (booking_id, employee_id, role, assigned_by)
                VALUES (@id, @employeeId, 'owner', @userId)
                """, new { id, employeeId = input.AssignedEmployeeId, userId }, tx,
                cancellationToken: ct));

        await tx.CommitAsync(ct);
        return id;
    }

    public async Task<Booking> UpdateDraftAsync(Guid id, int version, BookingInput input,
        Guid userId, CancellationToken ct = default)
    {
        var lines = await BuildLinesAsync(input, ct);
        await using var connection = await _factory.OpenAsync(ct);
        await using var tx = await connection.BeginTransactionAsync(ct);
        await EnsureDraftEmployeeAsync(connection, tx, input.AssignedEmployeeId, ct);

        // الموظف المسؤول الحالي (لتسجيل الإسناد الافتراضي عند تغييره مع كل حفظ).
        var currentEmployee = await connection.QuerySingleOrDefaultAsync<Guid?>(
            new CommandDefinition(
                "SELECT assigned_employee_id FROM bookings " +
                "WHERE id = @id AND version = @version AND status = 'draft'",
                new { id, version }, tx, cancellationToken: ct));

        var updated = await connection.ExecuteAsync(new CommandDefinition("""
            UPDATE bookings SET booking_date = @BookingDate, customer_id = @CustomerId,
                due_date = @DueDate, starts_at = @StartsAt, ends_at = @EndsAt,
                expires_at = @ExpiresAt, assigned_employee_id = @AssignedEmployeeId,
                notes = @Notes,
                related_booking_id = @RelatedBookingId, total_amount = @total, updated_by = @userId
            WHERE id = @id AND version = @version AND status = 'draft'
            """, new
        {
            id, version, bookingDate = input.BookingDate.Date, input.CustomerId, input.DueDate,
            StartsAt = DbTime.Utc(input.StartsAt), EndsAt = DbTime.Utc(input.EndsAt),
            ExpiresAt = DbTime.Utc(input.ExpiresAt), input.AssignedEmployeeId, input.Notes,
            input.RelatedBookingId,
            total = lines.Sum(l => l.TotalAmount), userId
        }, tx, cancellationToken: ct));

        if (updated == 0) throw new ConflictException();

        // الإسناد الافتراضي: الموظف المسؤول يُسجَّل بدور owner عند كل حفظ يتغير فيه الموظف.
        if (input.AssignedEmployeeId != currentEmployee)
        {
            await connection.ExecuteAsync(new CommandDefinition("""
                UPDATE booking_assignments SET unassigned_at = now()
                WHERE booking_id = @id AND role = 'owner' AND unassigned_at IS NULL
                """, new { id }, tx, cancellationToken: ct));
            if (input.AssignedEmployeeId is { } employeeId)
                await connection.ExecuteAsync(new CommandDefinition("""
                    INSERT INTO booking_assignments (booking_id, employee_id, role, assigned_by)
                    VALUES (@id, @employeeId, 'owner', @userId)
                    """, new { id, employeeId, userId }, tx, cancellationToken: ct));
        }

        await connection.ExecuteAsync(new CommandDefinition(
            "DELETE FROM booking_lines WHERE booking_id = @id", new { id }, tx,
            cancellationToken: ct));
        await InsertLinesAsync(connection, tx, id, lines, ct);

        await tx.CommitAsync(ct);
        return (await GetAsync(id, ct))!;
    }

    public async Task<Booking> ConfirmAsync(Guid id, Guid userId, bool reserveStock,
        CancellationToken ct = default)
    {
        await using var connection = await _factory.OpenAsync(ct);
        await using var tx = await connection.BeginTransactionAsync(ct);

        var head = await connection.QuerySingleOrDefaultAsync<(Guid Id, Guid BranchId,
            DateTime BookingDate, string Status)>(new CommandDefinition(
            "SELECT id, branch_id, booking_date, status FROM bookings WHERE id = @id FOR UPDATE",
            new { id }, tx, cancellationToken: ct));
        if (head.Id == Guid.Empty) throw new NotFoundAppException();
        if (head.Status != BookingStatuses.Draft)
            throw new ValidationAppException("validation.booking_not_draft");

        // تثبيت الموظف المسؤول من موعد العميل إن لم يكن مسجلاً، ثم التحقق من الجدول.
        await EnsureResponsibleEmployeeAsync(connection, tx, id, userId, ct);
        await EnsureScheduledEmployeeAsync(connection, tx, id, ct);

        var lines = (await connection.QueryAsync<(Guid Id, Guid ItemId, Guid WarehouseId,
            decimal Quantity, string? ItemCode)>(new CommandDefinition("""
            SELECT l.id, l.item_id, l.warehouse_id, l.quantity, i.code AS item_code
            FROM booking_lines l JOIN items i ON i.id = l.item_id
            WHERE l.booking_id = @id ORDER BY l.line_no
            """, new { id }, tx, cancellationToken: ct))).ToList();
        if (lines.Count == 0)
            throw new ValidationAppException("validation.document_has_no_lines");

        foreach (var line in lines)
        {
            if (reserveStock)
            {
                var available = await connection.ExecuteScalarAsync<decimal>(new CommandDefinition(
                    "SELECT COALESCE(fn_stock_available(@itemId, @warehouseId), 0)",
                    new { itemId = line.ItemId, warehouseId = line.WarehouseId }, tx,
                    cancellationToken: ct));
                if (available < line.Quantity)
                    throw new ValidationAppException("validation.booking_not_available",
                        new Dictionary<string, string>
                        {
                            ["item"] = line.ItemCode ?? "",
                            ["available"] = available.ToString("N4")
                        });

                await connection.ExecuteAsync(new CommandDefinition("""
                    UPDATE stock_balances SET reserved_qty = reserved_qty + @quantity,
                        updated_at = now()
                    WHERE item_id = @itemId AND warehouse_id = @warehouseId
                    """, new { quantity = line.Quantity, itemId = line.ItemId,
                        warehouseId = line.WarehouseId }, tx, cancellationToken: ct));
            }

            // كمية العميل تُسجّل دائمًا في كارتة الحجوزات؛ حجز المخزون الفعلي مشروط بالإعداد.
            await connection.ExecuteAsync(new CommandDefinition("""
                UPDATE booking_lines
                   SET reserved_qty = GREATEST(quantity - delivered_qty - cancelled_qty, 0)
                 WHERE id = @lineId
                """, new { lineId = line.Id }, tx, cancellationToken: ct));
        }

        var docNo = await connection.ExecuteScalarAsync<string>(new CommandDefinition("""
            SELECT next_document_number('BOOK'::varchar, @branchId::uuid, @docDate::date,
                                        'BK'::varchar, 6::smallint)
            """, new { branchId = head.BranchId, docDate = head.BookingDate }, tx,
            cancellationToken: ct));

        await connection.ExecuteAsync(new CommandDefinition("""
            UPDATE bookings SET status = 'confirmed', doc_no = @docNo, updated_by = @userId
            WHERE id = @id
            """, new { docNo, userId, id }, tx, cancellationToken: ct));

        await tx.CommitAsync(ct);
        return (await GetAsync(id, ct))!;
    }

    public async Task<Booking> CancelAsync(Guid id, string reason, Guid userId,
        CancellationToken ct = default)
    {
        await using var connection = await _factory.OpenAsync(ct);
        await using var tx = await connection.BeginTransactionAsync(ct);

        var head = await connection.QuerySingleOrDefaultAsync<(Guid Id, string Status, decimal Delivered)>(
            new CommandDefinition("""
            SELECT b.id, b.status, COALESCE((SELECT SUM(l.delivered_qty) FROM booking_lines l
                                             WHERE l.booking_id = b.id), 0) AS delivered
            FROM bookings b WHERE b.id = @id FOR UPDATE OF b
            """, new { id }, tx, cancellationToken: ct));
        if (head.Id == Guid.Empty) throw new NotFoundAppException();
        if (head.Status is not (BookingStatuses.Draft or BookingStatuses.Confirmed))
            throw new ValidationAppException("validation.booking_not_cancellable");
        if (head.Delivered > 0)
            throw new ValidationAppException("validation.booking_has_deliveries");

        await connection.ExecuteAsync(new CommandDefinition("""
            UPDATE stock_balances sb SET reserved_qty = GREATEST(sb.reserved_qty - l.reserved_qty, 0),
                updated_at = now()
            FROM booking_lines l
            WHERE l.booking_id = @id AND l.reserved_qty > 0
              AND sb.item_id = l.item_id AND sb.warehouse_id = l.warehouse_id
            """, new { id }, tx, cancellationToken: ct));
        await connection.ExecuteAsync(new CommandDefinition(
            "UPDATE booking_lines SET reserved_qty = 0 WHERE booking_id = @id", new { id }, tx,
            cancellationToken: ct));

        await connection.ExecuteAsync(new CommandDefinition("""
            UPDATE bookings SET status = 'cancelled', reversed_at = now(), reversed_by = @userId,
                reverse_reason = @reason, updated_by = @userId
            WHERE id = @id
            """, new { userId, reason, id }, tx, cancellationToken: ct));

        await tx.CommitAsync(ct);
        return (await GetAsync(id, ct))!;
    }
}
