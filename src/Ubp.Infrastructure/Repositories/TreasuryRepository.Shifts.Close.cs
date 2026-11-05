using Dapper;
using Npgsql;
using Ubp.Core.Models;
using Ubp.Core.Results;

namespace Ubp.Infrastructure.Repositories;

public sealed partial class TreasuryRepository
{
    /// <summary>
    /// إغلاق وردية الفرع: المتوقع = افتتاحي + صافي حركة النقد في كل خزائن الفرع،
    /// ويُحصر كل ما سُجّل خلال الوردية (مسودات ومرحّل، غير الملغى) في
    /// shift_review_items ليُراجَع بواسطة يوزر محاسب. countedCash = null إغلاق آلي.
    /// </summary>
    public async Task<Shift> CloseShiftAsync(Guid shiftId, decimal? countedCash, Guid userId,
        bool autoClosed, CancellationToken ct = default)
    {
        if (countedCash is < 0)
            throw new ValidationAppException("validation.shift_counted_cash");

        await using var connection = await _factory.OpenAsync(ct);
        await using var tx = await connection.BeginTransactionAsync(ct);

        var shift = await connection.QuerySingleOrDefaultAsync<ShiftRow>(new CommandDefinition(
            ShiftSelect + " WHERE s.id = @shiftId FOR UPDATE OF s", new { shiftId }, tx,
            cancellationToken: ct)) ?? throw new NotFoundAppException();

        if (shift.Status != ShiftStatuses.Open)
            throw new ValidationAppException("validation.shift_not_open");

        var movement = await CashMovementAsync(connection, tx, shift.BranchId, shift.OpenedAt, ct);

        var expected = Math.Round(shift.OpeningCash + movement, 2, MidpointRounding.AwayFromZero);
        var counted = Math.Round(countedCash ?? expected, 2, MidpointRounding.AwayFromZero);
        var variance = Math.Round(counted - expected, 2, MidpointRounding.AwayFromZero);

        if (variance != 0)
            await PostOverShortAsync(connection, tx, shift, variance, userId, ct);

        await CaptureReviewItemsAsync(connection, tx, shift, ct);
        var pending = await connection.ExecuteScalarAsync<int>(new CommandDefinition(
            "SELECT count(*)::int FROM shift_review_items WHERE shift_id = @shiftId AND status = 'pending'",
            new { shiftId }, tx, cancellationToken: ct));

        await connection.ExecuteAsync(new CommandDefinition("""
            UPDATE cashier_shifts SET expected_cash = @expected, counted_cash = @counted,
                variance = @variance, status = 'closed', closed_at = now(), closed_by = @userId,
                auto_closed = @autoClosed,
                review_status = CASE WHEN @pending > 0 THEN 'pending' ELSE 'none' END,
                updated_by = @userId
            WHERE id = @shiftId
            """, new
        {
            expected, counted, variance, userId, autoClosed, pending, shiftId
        }, tx, cancellationToken: ct));

        await tx.CommitAsync(ct);
        return (await GetShiftAsync(shiftId, ct))!;
    }

    /// <summary>صافي حركة النقد في كل خزائن الفرع منذ فتح الوردية (نقدي فقط).</summary>
    private static async Task<decimal> CashMovementAsync(NpgsqlConnection connection,
        NpgsqlTransaction? tx, Guid branchId, DateTimeOffset openedAt, CancellationToken ct) =>
        await connection.ExecuteScalarAsync<decimal>(new CommandDefinition("""
            SELECT COALESCE(SUM(CASE
                       WHEN v.voucher_type = 'receipt' AND m.method_type = 'cash'
                            AND st.branch_id = @branchId THEN l.amount - l.fee_amount
                       WHEN v.voucher_type = 'payment' AND m.method_type = 'cash'
                            AND st.branch_id = @branchId THEN -(l.amount - l.fee_amount)
                       WHEN v.voucher_type = 'transfer' AND m.method_type = 'cash'
                            AND st.branch_id = @branchId
                            AND (tt.branch_id IS NULL OR tt.branch_id <> @branchId)
                           THEN -(l.amount + l.fee_amount)
                       WHEN v.voucher_type = 'transfer' AND m.method_type = 'cash'
                            AND tt.branch_id = @branchId
                            AND (st.branch_id IS NULL OR st.branch_id <> @branchId)
                           THEN l.amount
                       ELSE 0 END), 0)
            FROM voucher_lines l
            JOIN vouchers v ON v.id = l.voucher_id
            JOIN treasury_methods m ON m.id = l.treasury_method_id
            JOIN treasuries st ON st.id = v.treasury_id
            LEFT JOIN treasuries tt ON tt.id = v.to_treasury_id
            WHERE v.status = 'posted' AND v.posted_at >= @openedAt
              AND (st.branch_id = @branchId OR tt.branch_id = @branchId)
            """, new { branchId, openedAt }, tx, cancellationToken: ct));

    /// <summary>قيد زيادة/عجز الوردية على حساب الخزينة الرئيسية للفرع (أو أول خزينة).</summary>
    private static async Task PostOverShortAsync(NpgsqlConnection connection, NpgsqlTransaction tx,
        ShiftRow shift, decimal variance, Guid userId, CancellationToken ct)
    {
        var treasury = await connection.QuerySingleOrDefaultAsync<TreasuryRow>(new CommandDefinition("""
            SELECT id AS Id, branch_id AS BranchId, account_id AS AccountId,
                   treasury_type AS TreasuryType
            FROM treasuries
            WHERE branch_id = @branchId AND is_active
            ORDER BY is_main DESC, code
            LIMIT 1
            """, new { branchId = shift.BranchId }, tx, cancellationToken: ct))
            ?? throw new ValidationAppException("validation.treasury_not_found");

        var defaults = (await connection.QueryAsync<(string Key, Guid? AccountId)>(
            new CommandDefinition("SELECT setting_key, account_id FROM posting_defaults",
                transaction: tx, cancellationToken: ct)))
            .Where(r => r.AccountId.HasValue)
            .ToDictionary(r => r.Key, r => r.AccountId!.Value, StringComparer.OrdinalIgnoreCase);

        if (!defaults.TryGetValue(PostingKeys.CashOverShort, out var overShort))
            throw new ValidationAppException("validation.posting_account_missing",
                new Dictionary<string, string>
                {
                    ["key"] = PostingKeys.CashOverShort, ["module"] = "treasury"
                });

        var amount = Math.Abs(variance);
        var gl = variance > 0
            ? new List<JournalLine>
            {
                new(treasury.AccountId, amount, 0),
                new(overShort, 0, amount)
            }
            : new List<JournalLine>
            {
                new(overShort, amount, 0),
                new(treasury.AccountId, 0, amount)
            };

        await CreateVoucherEntryAsync(connection, tx, Guid.CreateVersion7(), treasury.TreasuryType,
            shift.BranchId, DateTime.Today, shift.Code,
            $"{(variance > 0 ? "زيادة" : "عجز")} وردية {shift.Code}", gl, userId, null, ct,
            "cash_shift");
    }
}

