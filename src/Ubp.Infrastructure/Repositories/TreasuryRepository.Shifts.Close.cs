using Dapper;
using Npgsql;
using Ubp.Core.Models;
using Ubp.Core.Results;

namespace Ubp.Infrastructure.Repositories;

public sealed partial class TreasuryRepository
{
    /// <summary>
    /// إغلاق وردية الفرع (0076): المتوقع لكل خزينة = لقطة افتتاحها + حركتها خلال الوردية،
    /// والنقد الفعلي يُدخل يدوياً لكل خزينة ويُحفظ **مسودة** (draft) مع الفرق — بلا أي
    /// ترحيل للحسابات؛ الترحيل يتم باعتماد المحاسب. counts = null يعني إغلاقاً آلياً.
    /// </summary>
    public async Task<Shift> CloseShiftAsync(Guid shiftId,
        IReadOnlyList<ShiftCashCountInput>? counts, Guid userId, bool autoClosed,
        CancellationToken ct = default)
    {
        if (counts is not null && counts.Any(c => c.CountedCash < 0))
            throw new ValidationAppException("validation.shift_counted_cash");

        await using var connection = await _factory.OpenAsync(ct);
        await using var tx = await connection.BeginTransactionAsync(ct);

        var shift = await connection.QuerySingleOrDefaultAsync<ShiftRow>(new CommandDefinition(
            ShiftSelect + " WHERE s.id = @shiftId FOR UPDATE OF s", new { shiftId }, tx,
            cancellationToken: ct)) ?? throw new NotFoundAppException();
        if (shift.Status != ShiftStatuses.Open)
            throw new ValidationAppException("validation.shift_not_open");

        var rows = (await QueryCashCountsAsync(connection, shiftId, tx, ct)).ToList();
        decimal totalExpected = 0, totalCounted = 0, totalVariance = 0;

        foreach (var row in rows)
        {
            var movement = await TreasuryMovementAsync(connection, tx, row.TreasuryId,
                shift.OpenedAt, ct);
            // النقد المتوقع = أول المدة + (المحصل − المنصرف) للخزينة خلال الوردية (0079).
            var expected = Math.Round(row.OpeningCash + movement, 2,
                MidpointRounding.AwayFromZero);
            var counted = counts?.FirstOrDefault(c => c.TreasuryId == row.TreasuryId)?.CountedCash
                          ?? expected;
            counted = Math.Round(counted, 2, MidpointRounding.AwayFromZero);
            var variance = Math.Round(counted - expected, 2, MidpointRounding.AwayFromZero);

            await connection.ExecuteAsync(new CommandDefinition("""
                UPDATE shift_cash_counts
                SET expected_cash = @expected, counted_cash = @counted, variance = @variance,
                    status = 'draft'
                WHERE id = @id
                """, new { expected, counted, variance, id = row.Id }, tx, cancellationToken: ct));

            totalExpected += expected; totalCounted += counted; totalVariance += variance;
        }

        // خزائن أُضيفت بعد فتح الوردية: تُسجَّل مسودة كذلك حتى تُراجَع.
        foreach (var input in (counts ?? Array.Empty<ShiftCashCountInput>())
                 .Where(c => rows.All(r => r.TreasuryId != c.TreasuryId)))
        {
            await connection.ExecuteAsync(new CommandDefinition("""
                INSERT INTO shift_cash_counts (id, shift_id, treasury_id, opening_cash,
                    expected_cash, counted_cash, variance, status)
                VALUES (@id, @shiftId, @treasuryId, 0, 0, @counted, @counted, 'draft')
                ON CONFLICT (shift_id, treasury_id) DO NOTHING
                """, new
            {
                id = Guid.CreateVersion7(), shiftId, treasuryId = input.TreasuryId,
                counted = Math.Round(input.CountedCash, 2, MidpointRounding.AwayFromZero)
            }, tx, cancellationToken: ct));
            totalCounted += input.CountedCash;
            totalVariance += input.CountedCash;
        }

        await CaptureReviewItemsAsync(connection, tx, shift, ct);

        await connection.ExecuteAsync(new CommandDefinition("""
            UPDATE cashier_shifts SET expected_cash = @expected, counted_cash = @counted,
                variance = @variance, status = 'closed', closed_at = now(), closed_by = @userId,
                auto_closed = @autoClosed, review_status = 'pending', updated_by = @userId
            WHERE id = @shiftId
            """, new
        {
            expected = Math.Round(totalExpected, 2), counted = Math.Round(totalCounted, 2),
            variance = Math.Round(totalVariance, 2), userId, autoClosed, shiftId
        }, tx, cancellationToken: ct));

        await tx.CommitAsync(ct);
        return (await GetShiftAsync(shiftId, ct))!;
    }

    /// <summary>صافي حركة خزينة واحدة منذ فتح الوردية (المرحّل + المسودات، أياً كانت طريقة الدفع).</summary>
    private static async Task<decimal> TreasuryMovementAsync(NpgsqlConnection connection,
        NpgsqlTransaction? tx, Guid treasuryId, DateTimeOffset openedAt, CancellationToken ct) =>
        await connection.ExecuteScalarAsync<decimal>(new CommandDefinition("""
            SELECT COALESCE(SUM(CASE
                       WHEN v.treasury_id = @treasuryId AND v.voucher_type = 'receipt'
                           THEN v.total_amount
                       WHEN v.treasury_id = @treasuryId AND v.voucher_type = 'payment'
                           THEN -v.total_amount
                       WHEN v.treasury_id = @treasuryId AND v.voucher_type = 'transfer'
                           THEN -(v.total_amount + v.fee_total)
                       WHEN v.to_treasury_id = @treasuryId AND v.voucher_type = 'transfer'
                           THEN v.total_amount
                       ELSE 0 END), 0)
            FROM vouchers v
            WHERE v.status <> 'cancelled' AND v.created_at >= @openedAt
              AND (v.treasury_id = @treasuryId OR v.to_treasury_id = @treasuryId)
            """, new { treasuryId, openedAt }, tx, cancellationToken: ct));

    /// <summary>عدّ النقدية لكل خزينة في الوردية.</summary>
    private static async Task<IReadOnlyList<ShiftCashCount>> QueryCashCountsAsync(
        NpgsqlConnection connection, Guid shiftId, NpgsqlTransaction? tx,
        CancellationToken ct) =>
        (await connection.QueryAsync<ShiftCashCount>(new CommandDefinition("""
            SELECT c.id AS Id, c.shift_id AS ShiftId, c.treasury_id AS TreasuryId,
                   t.code AS TreasuryCode, t.name_ar AS TreasuryName,
                   t.treasury_type AS TreasuryType, c.opening_cash AS OpeningCash,
                   c.expected_cash AS ExpectedCash, c.counted_cash AS CountedCash,
                   c.variance AS Variance, c.status AS Status
            FROM shift_cash_counts c
            JOIN treasuries t ON t.id = c.treasury_id
            WHERE c.shift_id = @shiftId
            ORDER BY t.code
            """, new { shiftId }, tx, cancellationToken: ct))).ToList();

    /// <summary>تحديث القراءة الفعلية لعداد جهاز مع تسجيل قراءة في سجل العدّاد — 0078.</summary>
    public async Task<bool> SetMeterReadingAsync(Guid assetId, decimal value, Guid userId,
        CancellationToken ct = default)
    {
        if (value < 0) throw new ValidationAppException("validation.meter_units_required");
        await using var connection = await _factory.OpenAsync(ct);
        await using var tx = await connection.BeginTransactionAsync(ct);
        var updated = await connection.ExecuteAsync(new CommandDefinition(
            "UPDATE fixed_assets SET current_meter_value = @value, updated_by = @userId " +
            "WHERE id = @assetId AND is_metered", new { assetId, value, userId }, tx,
            cancellationToken: ct));
        if (updated == 0) { await tx.RollbackAsync(ct); return false; }
        await connection.ExecuteAsync(new CommandDefinition("""
            INSERT INTO asset_meter_readings (asset_id, value, delta, source, created_by)
            VALUES (@assetId, @value, 0, 'MANUAL', @userId)
            """, new { assetId, value, userId }, tx, cancellationToken: ct));
        await tx.CommitAsync(ct);
        return true;
    }

    /// <summary>حفظ ملاحظات الوردية لتظهر في العرض والإغلاق كل مرة — 0078.</summary>
    public async Task<bool> SaveShiftNotesAsync(Guid shiftId, string? notes,
        CancellationToken ct = default)
    {
        await using var connection = await _factory.OpenAsync(ct);
        return await connection.ExecuteAsync(new CommandDefinition(
            "UPDATE cashier_shifts SET notes = @notes WHERE id = @shiftId",
            new { shiftId, notes }, cancellationToken: ct)) > 0;
    }

    public async Task<IReadOnlyList<ShiftCashCount>> ListCashCountsAsync(Guid shiftId,
        CancellationToken ct = default)
    {
        await using var connection = await _factory.OpenAsync(ct);
        return await QueryCashCountsAsync(connection, shiftId, null, ct);
    }
}

