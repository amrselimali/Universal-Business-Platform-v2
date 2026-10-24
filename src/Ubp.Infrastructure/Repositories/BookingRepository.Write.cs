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

        await connection.ExecuteAsync(new CommandDefinition("""
            INSERT INTO bookings (id, branch_id, booking_date, status, customer_id, source, due_date,
                starts_at, ends_at, expires_at, assigned_employee_id, related_booking_id,
                total_amount, notes, created_by)
            VALUES (@id, @BranchId, @BookingDate, 'draft', @CustomerId, 'manual', @DueDate,
                    @StartsAt, @EndsAt, @ExpiresAt, @AssignedEmployeeId, @RelatedBookingId,
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

    public async Task<Booking> UpdateDraftAsync(Guid id, int version, BookingInput input,
        Guid userId, CancellationToken ct = default)
    {
        var lines = await BuildLinesAsync(input, ct);
        await using var connection = await _factory.OpenAsync(ct);
        await using var tx = await connection.BeginTransactionAsync(ct);

        var updated = await connection.ExecuteAsync(new CommandDefinition("""
            UPDATE bookings SET booking_date = @BookingDate, customer_id = @CustomerId,
                due_date = @DueDate, starts_at = @StartsAt, ends_at = @EndsAt,
                expires_at = @ExpiresAt, notes = @Notes,
                related_booking_id = @RelatedBookingId, total_amount = @total, updated_by = @userId
            WHERE id = @id AND version = @version AND status = 'draft'
            """, new
        {
            id, version, bookingDate = input.BookingDate.Date, input.CustomerId, input.DueDate,
            StartsAt = DbTime.Utc(input.StartsAt), EndsAt = DbTime.Utc(input.EndsAt),
            ExpiresAt = DbTime.Utc(input.ExpiresAt), input.Notes, input.RelatedBookingId,
            total = lines.Sum(l => l.TotalAmount), userId
        }, tx, cancellationToken: ct));

        if (updated == 0) throw new ConflictException();

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

        var lines = (await connection.QueryAsync<(Guid Id, Guid ItemId, Guid WarehouseId,
            decimal Quantity, string? ItemCode)>(new CommandDefinition("""
            SELECT l.id, l.item_id, l.warehouse_id, l.quantity, i.code AS item_code
            FROM booking_lines l JOIN items i ON i.id = l.item_id
            WHERE l.booking_id = @id ORDER BY l.line_no
            """, new { id }, tx, cancellationToken: ct))).ToList();
        if (lines.Count == 0)
            throw new ValidationAppException("validation.document_has_no_lines");

        if (reserveStock)
        {
            foreach (var line in lines)
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
                await connection.ExecuteAsync(new CommandDefinition(
                    "UPDATE booking_lines SET reserved_qty = quantity WHERE id = @lineId",
                    new { lineId = line.Id }, tx, cancellationToken: ct));
            }
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

    public async Task<Booking> AssignEmployeeAsync(Guid id, Guid employeeId, string role,
        Guid userId, CancellationToken ct = default)
    {
        if (role is not ("owner" or "executor"))
            throw new ValidationAppException("validation.assignment_role");

        await using var connection = await _factory.OpenAsync(ct);
        await using var tx = await connection.BeginTransactionAsync(ct);

        var status = await connection.ExecuteScalarAsync<string?>(new CommandDefinition(
            "SELECT status FROM bookings WHERE id = @id FOR UPDATE", new { id }, tx,
            cancellationToken: ct));
        if (status is null) throw new NotFoundAppException();
        if (status is BookingStatuses.Completed or BookingStatuses.Cancelled)
            throw new ValidationAppException("validation.booking_closed");

        await connection.ExecuteAsync(new CommandDefinition("""
            UPDATE booking_assignments SET unassigned_at = now()
            WHERE booking_id = @id AND role = @role AND unassigned_at IS NULL
            """, new { id, role }, tx, cancellationToken: ct));
        await connection.ExecuteAsync(new CommandDefinition("""
            INSERT INTO booking_assignments (booking_id, employee_id, role, assigned_by)
            VALUES (@id, @employeeId, @role, @userId)
            """, new { id, employeeId, role, userId }, tx, cancellationToken: ct));

        if (role == "owner")
            await connection.ExecuteAsync(new CommandDefinition(
                "UPDATE bookings SET assigned_employee_id = @employeeId, updated_by = @userId " +
                "WHERE id = @id", new { employeeId, userId, id }, tx, cancellationToken: ct));

        await tx.CommitAsync(ct);
        return (await GetAsync(id, ct))!;
    }
}
