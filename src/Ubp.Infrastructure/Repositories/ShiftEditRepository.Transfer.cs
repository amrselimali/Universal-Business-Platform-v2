using Dapper;
using Npgsql;
using Ubp.Core.Models;
using Ubp.Core.Results;

namespace Ubp.Infrastructure.Repositories;

public sealed partial class ShiftEditRepository
{
    private static async Task<bool> ApplyTransferAsync(NpgsqlConnection connection,
        NpgsqlTransaction tx, ShiftLineEditInput input, CancellationToken ct)
    {
        if (input.LineId is not { } lineId)
            throw new ValidationAppException("validation.required_fields");

        var tl = await connection.QuerySingleOrDefaultAsync<(Guid TransferId, int LineNo,
            Guid FromBookingLineId, decimal UnitPrice, decimal Quantity)>(new CommandDefinition("""
            SELECT transfer_id AS TransferId, line_no AS LineNo,
                   from_booking_line_id AS FromBookingLineId, unit_price AS UnitPrice,
                   quantity AS Quantity
            FROM service_transfer_lines WHERE id = @id FOR UPDATE
            """, new { id = lineId }, tx, cancellationToken: ct));
        if (tl.TransferId == Guid.Empty) throw new NotFoundAppException();

        var head = await connection.QuerySingleOrDefaultAsync<(string Status, Guid? TargetBookingId)>(
            new CommandDefinition(
                "SELECT status AS Status, target_booking_id AS TargetBookingId " +
                "FROM service_transfers WHERE id = @id FOR UPDATE", new { id = tl.TransferId }, tx,
                cancellationToken: ct));
        if (head.Status != ServiceTransferStatuses.Draft)
            throw new ValidationAppException("validation.shift_edit_locked");

        var newQty = Dec(input.NewValue);
        if (newQty <= 0) throw new ValidationAppException("validation.quantity_invalid");
        var delta = newQty - tl.Quantity;
        if (delta == 0) return true;

        Guid? targetLine = null;
        if (head.TargetBookingId is { } tb)
        {
            targetLine = await connection.ExecuteScalarAsync<Guid?>(new CommandDefinition(
                "SELECT id FROM booking_lines WHERE booking_id = @b AND line_no = @lineNo",
                new { b = tb, lineNo = tl.LineNo }, tx, cancellationToken: ct));
            if (targetLine is { } tlId)
            {
                var delivered = await connection.ExecuteScalarAsync<decimal>(new CommandDefinition(
                    "SELECT delivered_qty FROM booking_lines WHERE id = @id", new { id = tlId }, tx,
                    cancellationToken: ct));
                if (delivered > 0)
                    throw new ValidationAppException("validation.shift_edit_locked");
            }
        }

        if (delta > 0)
        {
            var affected = await connection.ExecuteAsync(new CommandDefinition("""
                UPDATE booking_lines
                SET reserved_qty = reserved_qty - @delta, transferred_qty = transferred_qty + @delta
                WHERE id = @id AND reserved_qty >= @delta
                """, new { delta, id = tl.FromBookingLineId }, tx, cancellationToken: ct));
            if (affected == 0)
                throw new ValidationAppException("validation.transfer_quantity_exceeds");
        }
        else
        {
            var inc = -delta;
            await connection.ExecuteAsync(new CommandDefinition("""
                UPDATE booking_lines
                SET reserved_qty = reserved_qty + @inc, transferred_qty = transferred_qty - @inc
                WHERE id = @id
                """, new { inc, id = tl.FromBookingLineId }, tx, cancellationToken: ct));
        }

        var amount = Math.Round(newQty * tl.UnitPrice, 2, MidpointRounding.AwayFromZero);
        if (targetLine is { } target)
        {
            await connection.ExecuteAsync(new CommandDefinition("""
                UPDATE booking_lines
                SET quantity = @qty, reserved_qty = @qty, transferred_qty = -@qty,
                    net_amount = @amount, total_amount = @amount
                WHERE id = @id
                """, new { qty = newQty, amount, id = target }, tx, cancellationToken: ct));
            await connection.ExecuteAsync(new CommandDefinition("""
                UPDATE bookings SET total_amount = COALESCE(
                    (SELECT SUM(total_amount) FROM booking_lines WHERE booking_id = @b), 0)
                WHERE id = @b
                """, new { b = head.TargetBookingId }, tx, cancellationToken: ct));
        }

        await connection.ExecuteAsync(new CommandDefinition(
            "UPDATE service_transfer_lines SET quantity = @qty, amount = @amount WHERE id = @id",
            new { qty = newQty, amount, id = lineId }, tx, cancellationToken: ct));
        await connection.ExecuteAsync(new CommandDefinition("""
            UPDATE service_transfers
            SET total_quantity = COALESCE(
                    (SELECT SUM(quantity) FROM service_transfer_lines WHERE transfer_id = @t), 0),
                total_amount = COALESCE(
                    (SELECT SUM(amount) FROM service_transfer_lines WHERE transfer_id = @t), 0)
            WHERE id = @t AND status = 'draft'
            """, new { t = tl.TransferId }, tx, cancellationToken: ct));
        return true;
    }
}
