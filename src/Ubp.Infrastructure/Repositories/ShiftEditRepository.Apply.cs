using System.Globalization;
using Dapper;
using Npgsql;
using Ubp.Core.Models;
using Ubp.Core.Results;
using Ubp.Infrastructure.Data;

namespace Ubp.Infrastructure.Repositories;

public sealed partial class ShiftEditRepository
{
    private static decimal Dec(string? v) =>
        decimal.TryParse(v, NumberStyles.Any, CultureInfo.InvariantCulture, out var d) ? d : 0m;

    private static Guid? GuidOrNull(string? v) => Guid.TryParse(v, out var g) ? g : null;

    /// <summary>يسمح بالتعديل أثناء الوردية المفتوحة أو أثناء انتظار المراجعة فقط (مسودات).</summary>
    public async Task<ShiftEditOutcome> ApplyAsync(ShiftLineEditInput input, Guid userId,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(input.EntityType) || string.IsNullOrWhiteSpace(input.Field))
            throw new ValidationAppException("validation.required_fields");

        await using var connection = await _factory.OpenAsync(ct);
        await using var tx = await connection.BeginTransactionAsync(ct);
        var s = await LoadShiftAsync(connection, input.ShiftId, tx, ct);
        if (s.Status != ShiftStatuses.Open && s.ReviewStatus != ShiftReviewStatuses.Pending)
            throw new ValidationAppException("validation.shift_edit_locked");

        var applied = input.EntityType switch
        {
            "voucher" => await ApplyVoucherAsync(connection, tx, input, ct),
            "operating_expense" => await ApplyExpenseAsync(connection, tx, input, ct),
            "commercial_document" => await ApplyCommercialAsync(connection, tx, input, ct),
            "booking" => await ApplyBookingAsync(connection, tx, input, ct),
            "service_transfer" => await ApplyTransferAsync(connection, tx, input, ct),
            _ => throw new ValidationAppException("validation.review_field_not_editable")
        };
        await tx.CommitAsync(ct);
        return new ShiftEditOutcome { Applied = applied };
    }

    private static async Task<bool> ApplyVoucherAsync(NpgsqlConnection connection,
        NpgsqlTransaction tx, ShiftLineEditInput input, CancellationToken ct)
    {
        var doc = await connection.QuerySingleOrDefaultAsync<(Guid Id, string Status)>(
            new CommandDefinition(
                "SELECT id AS Id, status AS Status FROM vouchers WHERE id = @id FOR UPDATE",
                new { id = input.DocId }, tx, cancellationToken: ct));
        if (doc.Id == Guid.Empty) throw new NotFoundAppException();
        if (doc.Status != VoucherStatuses.Draft)
            throw new ValidationAppException("validation.shift_edit_locked");

        if (input.Field == "amount" && input.LineId is { } lineId)
        {
            var amount = Dec(input.NewValue);
            if (amount <= 0) throw new ValidationAppException("validation.amount_invalid");
            await connection.ExecuteAsync(new CommandDefinition(
                "UPDATE voucher_lines SET amount = @amount WHERE id = @id AND voucher_id = @doc",
                new { amount, id = lineId, doc = input.DocId }, tx, cancellationToken: ct));
        }
        else if (input.Field == "treasury" && GuidOrNull(input.NewValue) is { } treasury)
        {
            await connection.ExecuteAsync(new CommandDefinition(
                "UPDATE vouchers SET treasury_id = @treasury WHERE id = @id",
                new { treasury, id = input.DocId }, tx, cancellationToken: ct));
        }
        else throw new ValidationAppException("validation.review_field_not_editable");

        await connection.ExecuteAsync(new CommandDefinition("""
            UPDATE vouchers SET total_amount = COALESCE(
                (SELECT SUM(amount) FROM voucher_lines WHERE voucher_id = @id), 0)
            WHERE id = @id
            """, new { id = input.DocId }, tx, cancellationToken: ct));
        return true;
    }

    private static async Task<bool> ApplyExpenseAsync(NpgsqlConnection connection,
        NpgsqlTransaction tx, ShiftLineEditInput input, CancellationToken ct)
    {
        var doc = await connection.QuerySingleOrDefaultAsync<(Guid Id, string Status)>(
            new CommandDefinition(
                "SELECT id AS Id, status AS Status FROM operating_expenses WHERE id = @id FOR UPDATE",
                new { id = input.DocId }, tx, cancellationToken: ct));
        if (doc.Id == Guid.Empty) throw new NotFoundAppException();
        if (doc.Status != "DRAFT") throw new ValidationAppException("validation.shift_edit_locked");

        if (input.Field == "treasury")
        {
            var treasury = GuidOrNull(input.NewValue)
                ?? throw new ValidationAppException("validation.required_fields");
            await connection.ExecuteAsync(new CommandDefinition(
                "UPDATE operating_expenses SET treasury_id = @t WHERE id = @id",
                new { t = treasury, id = input.DocId }, tx, cancellationToken: ct));
            return true;
        }
        if (input.LineId is not { } lineId)
            throw new ValidationAppException("validation.required_fields");

        var line = await connection.QuerySingleOrDefaultAsync<(decimal Qty, decimal Price)>(
            new CommandDefinition(
                "SELECT quantity AS Qty, unit_price AS Price FROM operating_expense_lines " +
                "WHERE id = @id FOR UPDATE", new { id = lineId }, tx, cancellationToken: ct));
        switch (input.Field)
        {
            case "description":
                await connection.ExecuteAsync(new CommandDefinition(
                    "UPDATE operating_expense_lines SET description = @v WHERE id = @id",
                    new { v = input.NewValue, id = lineId }, tx, cancellationToken: ct));
                break;
            case "account":
                var account = GuidOrNull(input.NewValue)
                    ?? throw new ValidationAppException("validation.required_fields");
                await connection.ExecuteAsync(new CommandDefinition(
                    "UPDATE operating_expense_lines SET account_id = @a WHERE id = @id",
                    new { a = account, id = lineId }, tx, cancellationToken: ct));
                break;
            case "quantity":
            {
                var qty = Dec(input.NewValue);
                if (qty <= 0) throw new ValidationAppException("validation.quantity_invalid");
                var amount = Math.Round(qty * line.Price, 2, MidpointRounding.AwayFromZero);
                await connection.ExecuteAsync(new CommandDefinition(
                    "UPDATE operating_expense_lines SET quantity = @qty, amount = @amount WHERE id = @id",
                    new { qty, amount, id = lineId }, tx, cancellationToken: ct));
                break;
            }
            case "unit_price":
            {
                var price = Dec(input.NewValue);
                if (price < 0) throw new ValidationAppException("validation.amount_invalid");
                var amount = Math.Round(line.Qty * price, 2, MidpointRounding.AwayFromZero);
                await connection.ExecuteAsync(new CommandDefinition(
                    "UPDATE operating_expense_lines SET unit_price = @price, amount = @amount WHERE id = @id",
                    new { price, amount, id = lineId }, tx, cancellationToken: ct));
                break;
            }
            case "amount":
            {
                var amount = Dec(input.NewValue);
                if (amount < 0) throw new ValidationAppException("validation.amount_invalid");
                await connection.ExecuteAsync(new CommandDefinition(
                    "UPDATE operating_expense_lines SET amount = @amount WHERE id = @id",
                    new { amount, id = lineId }, tx, cancellationToken: ct));
                break;
            }
            default:
                throw new ValidationAppException("validation.review_field_not_editable");
        }

        await connection.ExecuteAsync(new CommandDefinition("""
            UPDATE operating_expenses SET total_amount = COALESCE(
                (SELECT SUM(amount) FROM operating_expense_lines WHERE expense_id = @id), 0)
            WHERE id = @id
            """, new { id = input.DocId }, tx, cancellationToken: ct));
        return true;
    }
}
