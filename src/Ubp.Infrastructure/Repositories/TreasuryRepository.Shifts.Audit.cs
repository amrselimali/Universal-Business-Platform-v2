using Dapper;
using Npgsql;
using Ubp.Core.Models;
using Ubp.Core.Results;

namespace Ubp.Infrastructure.Repositories;

public sealed partial class TreasuryRepository
{
    public async Task<IReadOnlyList<ShiftReviewItem>> ListReviewItemsAsync(Guid shiftId,
        CancellationToken ct = default)
    {
        await using var connection = await _factory.OpenAsync(ct);
        return (await QueryReviewItemsAsync(connection, shiftId, ct)).ToList();
    }

    private static async Task<IReadOnlyList<ShiftReviewItem>> QueryReviewItemsAsync(
        NpgsqlConnection connection, Guid shiftId, CancellationToken ct) =>
        (await connection.QueryAsync<ShiftReviewItem>(new CommandDefinition("""
            SELECT i.id AS Id, i.shift_id AS ShiftId, i.screen_code AS ScreenCode,
                   i.entity_type AS EntityType, i.entity_id AS EntityId, i.doc_no AS DocNo,
                   i.doc_date AS DocDate, i.party_name AS PartyName, i.amount AS Amount,
                   i.doc_status AS DocStatus, i.status AS Status, i.reviewed_at AS ReviewedAt,
                   u.username AS ReviewedBy
            FROM shift_review_items i
            LEFT JOIN users u ON u.id = i.reviewed_by
            WHERE i.shift_id = @shiftId
            ORDER BY i.doc_date, i.doc_no, i.entity_id
            """, new { shiftId }, cancellationToken: ct))).ToList();

    /// <summary>ترحيل الوردية للحسابات: تعليمها «تحت المراجعة» حتى لو بلا بيانات — 0079.</summary>
    public async Task<bool> PostShiftAsync(Guid shiftId, Guid userId, CancellationToken ct = default)
    {
        await using var connection = await _factory.OpenAsync(ct);
        return await connection.ExecuteAsync(new CommandDefinition("""
            UPDATE cashier_shifts SET review_status = 'pending', updated_by = @userId
            WHERE id = @shiftId AND status = 'closed'
            """, new { shiftId, userId }, cancellationToken: ct)) > 0;
    }

    /// <summary>
    /// اعتماد مراجعة الوردية بواسطة يوزر محاسب (صلاحية APPROVE على شاشة الورديات):
    /// يُرحّل فرق كل خزينة (زيادة/عجز) على حساب الخزينة المخصص أو الافتراضي، ويحوّل
    /// مسودات عدّ النقدية إلى «posted»، ثم يغلق المراجعة — 0076.
    /// </summary>
    public async Task<Shift> ReviewShiftAsync(Guid shiftId, Guid userId, CancellationToken ct = default)
    {
        await using var connection = await _factory.OpenAsync(ct);
        await using var tx = await connection.BeginTransactionAsync(ct);

        var shift = await connection.QuerySingleOrDefaultAsync<ShiftRow>(new CommandDefinition(
            ShiftSelect + " WHERE s.id = @shiftId FOR UPDATE OF s", new { shiftId }, tx,
            cancellationToken: ct)) ?? throw new NotFoundAppException();
        if (shift.Status == ShiftStatuses.Open)
            throw new ValidationAppException("validation.shift_not_closed");

        var defaults = (await connection.QueryAsync<(string Key, Guid? AccountId)>(
            new CommandDefinition("SELECT setting_key, account_id FROM posting_defaults",
                transaction: tx, cancellationToken: ct)))
            .Where(r => r.AccountId.HasValue)
            .ToDictionary(r => r.Key, r => r.AccountId!.Value, StringComparer.OrdinalIgnoreCase);

        var counts = await connection.QueryAsync<(Guid Id, Guid TreasuryId, Guid AccountId,
            Guid? OverShortAccountId, string TreasuryType, decimal? Variance)>(
            new CommandDefinition("""
                SELECT c.id, c.treasury_id, t.account_id, t.over_short_account_id, t.treasury_type,
                       c.variance
                FROM shift_cash_counts c
                JOIN treasuries t ON t.id = c.treasury_id
                WHERE c.shift_id = @shiftId AND c.status = 'draft'
                FOR UPDATE OF c
                """, new { shiftId }, tx, cancellationToken: ct));

        foreach (var row in counts)
        {
            var variance = Math.Round(row.Variance ?? 0, 2, MidpointRounding.AwayFromZero);
            Guid? entryId = null;
            if (variance != 0)
            {
                var overShort = row.OverShortAccountId
                    ?? (defaults.TryGetValue(PostingKeys.CashOverShort, out var fallback)
                        ? fallback
                        : throw new ValidationAppException("validation.posting_account_missing",
                            new Dictionary<string, string>
                            {
                                ["key"] = PostingKeys.CashOverShort, ["module"] = "treasury"
                            }));

                var amount = Math.Abs(variance);
                var gl = variance > 0
                    ? new List<JournalLine>
                    {
                        new(row.AccountId, amount, 0), new(overShort, 0, amount)
                    }
                    : new List<JournalLine>
                    {
                        new(overShort, amount, 0), new(row.AccountId, 0, amount)
                    };

                await CreateVoucherEntryAsync(connection, tx, Guid.CreateVersion7(),
                    row.TreasuryType, shift.BranchId, DateTime.Today, shift.Code,
                    $"{(variance > 0 ? "زيادة" : "عجز")} وردية {shift.Code}", gl, userId,
                    row.Id, ct, "cash_shift");

                entryId = await connection.ExecuteScalarAsync<Guid?>(new CommandDefinition("""
                    SELECT id FROM journal_entries
                    WHERE source_type = 'cash_shift' AND source_id = @countId
                    """, new { countId = row.Id }, tx, cancellationToken: ct));
            }

            await connection.ExecuteAsync(new CommandDefinition("""
                UPDATE shift_cash_counts
                SET status = 'posted', posted_at = now(), posted_by = @userId,
                    journal_entry_id = @entryId
                WHERE id = @id
                """, new { userId, entryId, id = row.Id }, tx, cancellationToken: ct));
        }

        // ترحيل عمولة التحصيل بنسبة الخزينة إلى حسابها المربوط (0081).
        var fees = await connection.QueryAsync<(Guid TreasuryAccountId, Guid FeeAccountId,
            string TreasuryType, decimal Fee)>(new CommandDefinition("""
            SELECT t.account_id, t.collection_fee_account_id, t.treasury_type,
                   ROUND(SUM(v.total_amount) * MAX(t.collection_fee_percent) / 100, 2) AS fee
            FROM shift_cash_counts c
            JOIN cashier_shifts s ON s.id = c.shift_id
            JOIN treasuries t ON t.id = c.treasury_id
            JOIN vouchers v ON v.branch_id = s.branch_id AND v.treasury_id = t.id
                 AND v.voucher_type = 'receipt' AND v.status <> 'cancelled'
                 AND v.created_at >= s.opened_at
                 AND (s.closed_at IS NULL OR v.created_at <= s.closed_at)
            WHERE c.shift_id = @shiftId AND t.collection_fee_percent > 0
              AND t.collection_fee_account_id IS NOT NULL
            GROUP BY t.account_id, t.collection_fee_account_id, t.treasury_type
            HAVING ROUND(SUM(v.total_amount) * t.collection_fee_percent / 100, 2) > 0
            """, new { shiftId }, tx, cancellationToken: ct));

        foreach (var fee in fees)
        {
            var feeGl = new List<JournalLine>
            {
                new(fee.FeeAccountId, fee.Fee, 0), new(fee.TreasuryAccountId, 0, fee.Fee)
            };
            await CreateVoucherEntryAsync(connection, tx, Guid.CreateVersion7(),
                fee.TreasuryType, shift.BranchId, DateTime.Today, shift.Code,
                $"عمولة تحصيل وردية {shift.Code}", feeGl, userId, null, ct, "cash_shift");
        }

        await connection.ExecuteAsync(new CommandDefinition("""
            UPDATE shift_review_items
            SET status = 'reviewed', reviewed_at = now(), reviewed_by = @userId
            WHERE shift_id = @shiftId AND status = 'pending'
            """, new { shiftId, userId }, tx, cancellationToken: ct));

        await connection.ExecuteAsync(new CommandDefinition("""
            UPDATE cashier_shifts
            SET review_status = 'reviewed', reviewed_at = now(), reviewed_by = @userId,
                updated_by = @userId
            WHERE id = @shiftId
            """, new { shiftId, userId }, tx, cancellationToken: ct));

        await tx.CommitAsync(ct);
        return (await GetShiftAsync(shiftId, ct))!;
    }
}
