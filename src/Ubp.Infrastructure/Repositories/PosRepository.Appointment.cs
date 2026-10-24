using Dapper;
using Npgsql;
using Ubp.Core.Models;
using Ubp.Core.Results;

namespace Ubp.Infrastructure.Repositories;

public sealed partial class PosRepository
{
    /// <summary>حجز موعد خدمة: بلا سعر وبلا كمية وبلا تحصيل — وقت من/إلى فقط.</summary>
    public async Task<PosReceipt> BookAppointmentAsync(PosAppointmentInput input, Guid userId,
        CancellationToken ct = default)
    {
        if (input.BranchId == Guid.Empty || input.CustomerId == Guid.Empty ||
            input.WarehouseId == Guid.Empty || input.ItemId == Guid.Empty)
            throw new ValidationAppException("validation.required_fields");
        if (input.EndsAt <= input.StartsAt)
            throw new ValidationAppException("validation.booking_time_range");

        await using var connection = await _factory.OpenAsync(ct);
        await using var tx = await connection.BeginTransactionAsync(ct);

        var item = await connection.QuerySingleOrDefaultAsync<(Guid Id, string ItemType,
            Guid BaseUnitId)>(new CommandDefinition(
            "SELECT id, item_type, base_unit_id FROM items WHERE id = @id AND is_active",
            new { id = input.ItemId }, tx, cancellationToken: ct));
        if (item.Id == Guid.Empty)
            throw new ValidationAppException("validation.item_not_found");
        if (item.ItemType != "SERVICE")
            throw new ValidationAppException("validation.item_not_service");

        var bookingId = Guid.CreateVersion7();
        var bookingNo = await NextNumberAsync(connection, tx, "BOOK", input.BranchId,
            input.StartsAt.DateTime, "BK", ct);

        await connection.ExecuteAsync(new CommandDefinition("""
            INSERT INTO bookings (id, branch_id, doc_no, booking_date, status, customer_id, source,
                due_date, starts_at, ends_at, total_amount, notes, created_by)
            VALUES (@bookingId, @BranchId, @bookingNo, @bookingDate, 'confirmed', @CustomerId, 'pos',
                    @bookingDate, @StartsAt, @EndsAt, 0, @Notes, @userId)
            """, new { bookingId, input.BranchId, bookingNo,
                bookingDate = input.StartsAt.Date, input.CustomerId, input.StartsAt, input.EndsAt,
                input.Notes, userId }, tx, cancellationToken: ct));

        await connection.ExecuteAsync(new CommandDefinition("""
            INSERT INTO booking_lines (booking_id, line_no, item_id, unit_id, warehouse_id,
                quantity, unit_price, total_amount)
            VALUES (@bookingId, 1, @ItemId, @unitId, @WarehouseId, 1, 0, 0)
            """, new { bookingId, input.ItemId, unitId = item.BaseUnitId, input.WarehouseId }, tx,
            cancellationToken: ct));

        await tx.CommitAsync(ct);
        return new PosReceipt
        {
            BookingId = bookingId, BookingNo = bookingNo, Total = 0, Paid = 0
        };
    }
}
