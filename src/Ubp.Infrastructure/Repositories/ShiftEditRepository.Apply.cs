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

    /// <summary>يسمح بالتعديل أثناء الشفت المفتوحة أو أثناء انتظار المراجعة فقط (مسودات).</summary>
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
            "voucher" => await ApplyVoucherAsync(connection, tx, input, userId, ct),
            "operating_expense" => await ApplyExpenseAsync(connection, tx, input, userId, ct),
            "commercial_document" => await ApplyCommercialAsync(connection, tx, input, ct),
            "booking" => await ApplyBookingAsync(connection, tx, input, ct),
            "service_transfer" => await ApplyTransferAsync(connection, tx, input, ct),
            _ => throw new ValidationAppException("validation.review_field_not_editable")
        };
        await tx.CommitAsync(ct);
        return new ShiftEditOutcome { Applied = applied };
    }

    private static async Task<bool> ApplyVoucherAsync(NpgsqlConnection connection,
        NpgsqlTransaction tx, ShiftLineEditInput input, Guid userId, CancellationToken ct)
    {
        var doc = await connection.QuerySingleOrDefaultAsync<(Guid Id, Guid BranchId, string Status,
            Guid TreasuryId, string VoucherType, string? PartyType, Guid? PartyId, string? DocNo)>(
            new CommandDefinition("""
            SELECT id AS Id, branch_id AS BranchId, status AS Status, treasury_id AS TreasuryId,
                   voucher_type AS VoucherType, party_type::text AS PartyType, party_id AS PartyId,
                   doc_no AS DocNo
            FROM vouchers WHERE id = @id FOR UPDATE
            """, new { id = input.DocId }, tx, cancellationToken: ct));
        if (doc.Id == Guid.Empty) throw new NotFoundAppException();
        var isPosted = doc.Status == "posted";
        if (doc.Status is not ("draft" or "posted"))
            throw new ValidationAppException("validation.shift_edit_locked");

        if (input.Field == "treasury")
        {
            if (isPosted) throw new ValidationAppException("validation.shift_edit_locked");
            var treasury = GuidOrNull(input.NewValue)
                ?? throw new ValidationAppException("validation.required_fields");
            await connection.ExecuteAsync(new CommandDefinition(
                "UPDATE vouchers SET treasury_id = @treasury WHERE id = @id",
                new { treasury, id = input.DocId }, tx, cancellationToken: ct));
        }
        else if (input.Field == "amount" && input.LineId is { } lineId)
        {
            var oldAmount = await connection.ExecuteScalarAsync<decimal?>(new CommandDefinition(
                "SELECT amount FROM voucher_lines WHERE id = @id AND voucher_id = @doc",
                new { id = lineId, doc = input.DocId }, tx, cancellationToken: ct))
                ?? throw new ValidationAppException("validation.required_fields");
            var amount = Dec(input.NewValue);
            if (amount <= 0) throw new ValidationAppException("validation.amount_invalid");
            await connection.ExecuteAsync(new CommandDefinition(
                "UPDATE voucher_lines SET amount = @amount WHERE id = @id AND voucher_id = @doc",
                new { amount, id = lineId, doc = input.DocId }, tx, cancellationToken: ct));

            if (isPosted)
            {
                var delta = amount - oldAmount;
                var treasuryAcct = await connection.ExecuteScalarAsync<Guid>(new CommandDefinition(
                    "SELECT account_id FROM treasuries WHERE id = @id", new { id = doc.TreasuryId },
                    tx, cancellationToken: ct));
                var isReceipt = doc.VoucherType == VoucherTypes.Receipt;
                var advances = await NeedAccAsync(connection, tx,
                    isReceipt ? PostingKeys.CustomerAdvances : PostingKeys.SupplierAdvances, ct);
                Guid? customerId = isReceipt && doc.PartyType == "C" ? doc.PartyId : null;
                Guid? supplierId = !isReceipt && doc.PartyType == "S" ? doc.PartyId : null;
                if (delta != 0)
                {
                    var docNo = doc.DocNo ?? input.DocId.ToString("N")[..8];
                    var amt = Math.Abs(delta);
                    if (isReceipt)
                    {
                        if (delta > 0)
                            await PostDifferenceAsync(connection, tx, doc.BranchId, docNo,
                                treasuryAcct, null, null, advances, customerId, null, amt,
                                input.DocId, userId, ct);
                        else
                            await PostDifferenceAsync(connection, tx, doc.BranchId, docNo,
                                advances, customerId, null, treasuryAcct, null, null, amt,
                                input.DocId, userId, ct);
                    }
                    else
                    {
                        if (delta > 0)
                            await PostDifferenceAsync(connection, tx, doc.BranchId, docNo,
                                advances, null, supplierId, treasuryAcct, null, null, amt,
                                input.DocId, userId, ct);
                        else
                            await PostDifferenceAsync(connection, tx, doc.BranchId, docNo,
                                treasuryAcct, null, null, advances, null, supplierId, amt,
                                input.DocId, userId, ct);
                    }
                }
            }
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
        NpgsqlTransaction tx, ShiftLineEditInput input, Guid userId, CancellationToken ct)
    {
        var doc = await connection.QuerySingleOrDefaultAsync<(Guid Id, Guid BranchId, string Status,
            Guid? TreasuryId, Guid? SupplierId, string? DocNo)>(new CommandDefinition("""
            SELECT id AS Id, branch_id AS BranchId, status AS Status, treasury_id AS TreasuryId,
                   supplier_id AS SupplierId, doc_no AS DocNo
            FROM operating_expenses WHERE id = @id FOR UPDATE
            """, new { id = input.DocId }, tx, cancellationToken: ct));
        if (doc.Id == Guid.Empty) throw new NotFoundAppException();
        var isPosted = doc.Status == "POSTED";
        if (doc.Status is not ("DRAFT" or "POSTED"))
            throw new ValidationAppException("validation.shift_edit_locked");

        if (input.Field == "treasury")
        {
            if (isPosted) throw new ValidationAppException("validation.shift_edit_locked");
            var treasury = GuidOrNull(input.NewValue)
                ?? throw new ValidationAppException("validation.required_fields");
            await connection.ExecuteAsync(new CommandDefinition(
                "UPDATE operating_expenses SET treasury_id = @t WHERE id = @id",
                new { t = treasury, id = input.DocId }, tx, cancellationToken: ct));
            return true;
        }
        if (input.LineId is not { } lineId)
            throw new ValidationAppException("validation.required_fields");

        var line = await connection.QuerySingleOrDefaultAsync<(decimal Qty, decimal Price,
            decimal Amount, Guid AccountId)>(new CommandDefinition("""
            SELECT quantity AS Qty, unit_price AS Price, amount AS Amount, account_id AS AccountId
            FROM operating_expense_lines WHERE id = @id FOR UPDATE
            """, new { id = lineId }, tx, cancellationToken: ct));
        var delta = 0m;
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
                delta = amount - line.Amount;
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
                delta = amount - line.Amount;
                break;
            }
            case "amount":
            {
                var amount = Dec(input.NewValue);
                if (amount < 0) throw new ValidationAppException("validation.amount_invalid");
                await connection.ExecuteAsync(new CommandDefinition(
                    "UPDATE operating_expense_lines SET amount = @amount WHERE id = @id",
                    new { amount, id = lineId }, tx, cancellationToken: ct));
                delta = amount - line.Amount;
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

        // المستند المُرحَّل: قيد فرق بين حساب السطر وحساب الدفع/المورد بقيمة الفرق.
        if (isPosted && delta != 0)
        {
            Guid credit;
            Guid? supplierId = null;
            if (doc.TreasuryId is { } tid)
                credit = await connection.ExecuteScalarAsync<Guid>(new CommandDefinition(
                    "SELECT account_id FROM treasuries WHERE id = @id", new { id = tid }, tx,
                    cancellationToken: ct));
            else
            {
                credit = await NeedAccAsync(connection, tx, PostingKeys.SupplierPayable, ct);
                supplierId = doc.SupplierId;
            }
            var docNo = doc.DocNo ?? input.DocId.ToString("N")[..8];
            var amt = Math.Abs(delta);
            if (delta > 0)
                await PostDifferenceAsync(connection, tx, doc.BranchId, docNo,
                    line.AccountId, null, null, credit, null, supplierId, amt, input.DocId,
                    userId, ct);
            else
                await PostDifferenceAsync(connection, tx, doc.BranchId, docNo,
                    credit, null, supplierId, line.AccountId, null, null, amt, input.DocId,
                    userId, ct);
        }
        return true;
    }
}
