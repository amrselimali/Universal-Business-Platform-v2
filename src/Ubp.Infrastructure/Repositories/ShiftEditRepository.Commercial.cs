using System.Globalization;
using Dapper;
using Npgsql;
using Ubp.Core.Models;
using Ubp.Core.Results;

namespace Ubp.Infrastructure.Repositories;

public sealed partial class ShiftEditRepository
{
    private static async Task<decimal> TaxRateAsync(NpgsqlConnection connection,
        NpgsqlTransaction tx, Guid? taxId, CancellationToken ct) =>
        taxId is null ? 0m : await connection.ExecuteScalarAsync<decimal>(new CommandDefinition(
            "SELECT COALESCE(rate, 0) FROM taxes WHERE id = @id", new { id = taxId }, tx,
            cancellationToken: ct));

    private static async Task<bool> ApplyCommercialAsync(NpgsqlConnection connection,
        NpgsqlTransaction tx, ShiftLineEditInput input, CancellationToken ct)
    {
        var doc = await connection.QuerySingleOrDefaultAsync<(Guid Id, string Status)>(
            new CommandDefinition(
                "SELECT id AS Id, status AS Status FROM commercial_documents WHERE id = @id FOR UPDATE",
                new { id = input.DocId }, tx, cancellationToken: ct));
        if (doc.Id == Guid.Empty) throw new NotFoundAppException();
        if (doc.Status != "draft") throw new ValidationAppException("validation.shift_edit_locked");
        if (input.LineId is not { } lineId)
            throw new ValidationAppException("validation.required_fields");

        var line = await connection.QuerySingleOrDefaultAsync<(decimal Qty, decimal Price,
            decimal Disc, Guid? TaxId)>(new CommandDefinition("""
            SELECT quantity AS Qty, unit_price AS Price, discount_percent AS Disc, tax_id AS TaxId
            FROM commercial_document_lines WHERE id = @id FOR UPDATE
            """, new { id = lineId }, tx, cancellationToken: ct));

        if (input.Field == "description")
        {
            await connection.ExecuteAsync(new CommandDefinition(
                "UPDATE commercial_document_lines SET description = @v WHERE id = @id",
                new { v = input.NewValue, id = lineId }, tx, cancellationToken: ct));
        }
        else if (input.Field == "amount")
        {
            var amount = Dec(input.NewValue);
            if (amount < 0) throw new ValidationAppException("validation.amount_invalid");
            await connection.ExecuteAsync(new CommandDefinition("""
                UPDATE commercial_document_lines
                SET net_amount = @amount, tax_amount = 0, total_amount = @amount WHERE id = @id
                """, new { amount, id = lineId }, tx, cancellationToken: ct));
        }
        else if (input.Field is "quantity" or "unit_price")
        {
            var qty = input.Field == "quantity" ? Dec(input.NewValue) : line.Qty;
            var price = input.Field == "unit_price" ? Dec(input.NewValue) : line.Price;
            if (qty <= 0) throw new ValidationAppException("validation.quantity_invalid");
            if (price < 0) throw new ValidationAppException("validation.amount_invalid");
            var gross = qty * price;
            var discount = Math.Round(gross * line.Disc / 100m, 2, MidpointRounding.AwayFromZero);
            var net = Math.Round(gross - discount, 2, MidpointRounding.AwayFromZero);
            var rate = await TaxRateAsync(connection, tx, line.TaxId, ct);
            var tax = Math.Round(net * rate / 100m, 2, MidpointRounding.AwayFromZero);
            var total = Math.Round(net + tax, 2, MidpointRounding.AwayFromZero);
            await connection.ExecuteAsync(new CommandDefinition("""
                UPDATE commercial_document_lines
                SET quantity = @qty, unit_price = @price, net_amount = @net, tax_amount = @tax,
                    total_amount = @total
                WHERE id = @id
                """, new { qty, price, net, tax, total, id = lineId }, tx, cancellationToken: ct));
        }
        else throw new ValidationAppException("validation.review_field_not_editable");

        await connection.ExecuteAsync(new CommandDefinition("""
            UPDATE commercial_documents SET total = COALESCE(
                (SELECT SUM(total_amount) FROM commercial_document_lines WHERE document_id = @id), 0)
            WHERE id = @id
            """, new { id = input.DocId }, tx, cancellationToken: ct));
        return true;
    }
}
