using Dapper;
using Npgsql;
using Ubp.Core.Models;
using Ubp.Core.Results;

namespace Ubp.Infrastructure.Repositories;

public sealed partial class ShiftEditRepository
{
    private static async Task<bool> ApplyBookingAsync(NpgsqlConnection connection,
        NpgsqlTransaction tx, ShiftLineEditInput input, CancellationToken ct)
    {
        var doc = await connection.QuerySingleOrDefaultAsync<(Guid Id, string Status)>(
            new CommandDefinition(
                "SELECT id AS Id, status AS Status FROM bookings WHERE id = @id FOR UPDATE",
                new { id = input.DocId }, tx, cancellationToken: ct));
        if (doc.Id == Guid.Empty) throw new NotFoundAppException();
        if (doc.Status is not (BookingStatuses.Draft or BookingStatuses.Confirmed))
            throw new ValidationAppException("validation.shift_edit_locked");
        if (input.LineId is not { } lineId)
            throw new ValidationAppException("validation.required_fields");

        var line = await connection.QuerySingleOrDefaultAsync<(decimal Qty, decimal Price,
            decimal Disc, Guid? TaxId, decimal Delivered)>(new CommandDefinition("""
            SELECT quantity AS Qty, unit_price AS Price, discount_percent AS Disc,
                   tax_id AS TaxId, delivered_qty AS Delivered
            FROM booking_lines WHERE id = @id FOR UPDATE
            """, new { id = lineId }, tx, cancellationToken: ct));
        if (line.Delivered > 0) throw new ValidationAppException("validation.shift_edit_locked");

        if (input.Field == "employee")
        {
            var emp = GuidOrNull(input.NewValue);
            await connection.ExecuteAsync(new CommandDefinition(
                "UPDATE booking_lines SET employee_id = @emp WHERE id = @id",
                new { emp, id = lineId }, tx, cancellationToken: ct));
        }
        else if (input.Field is "quantity" or "unit_price")
        {
            var qty = input.Field == "quantity" ? Dec(input.NewValue) : line.Qty;
            var price = input.Field == "unit_price" ? Dec(input.NewValue) : line.Price;
            if (qty <= 0) throw new ValidationAppException("validation.quantity_invalid");
            if (price < 0) throw new ValidationAppException("validation.amount_invalid");
            var delta = qty - line.Qty;
            var gross = qty * price;
            var discount = Math.Round(gross * line.Disc / 100m, 2, MidpointRounding.AwayFromZero);
            var net = Math.Round(gross - discount, 2, MidpointRounding.AwayFromZero);
            var rate = await TaxRateAsync(connection, tx, line.TaxId, ct);
            var tax = Math.Round(net * rate / 100m, 2, MidpointRounding.AwayFromZero);
            var total = Math.Round(net + tax, 2, MidpointRounding.AwayFromZero);
            await connection.ExecuteAsync(new CommandDefinition("""
                UPDATE booking_lines
                SET quantity = @qty, unit_price = @price,
                    reserved_qty = GREATEST(reserved_qty + @delta, 0),
                    net_amount = @net, tax_amount = @tax, total_amount = @total
                WHERE id = @id
                """, new { qty, price, delta, net, tax, total, id = lineId }, tx,
                cancellationToken: ct));
        }
        else throw new ValidationAppException("validation.review_field_not_editable");

        await connection.ExecuteAsync(new CommandDefinition("""
            UPDATE bookings SET total_amount = COALESCE(
                (SELECT SUM(total_amount) FROM booking_lines WHERE booking_id = @id), 0)
            WHERE id = @id
            """, new { id = input.DocId }, tx, cancellationToken: ct));
        return true;
    }
}
