using Dapper;
using Npgsql;
using Ubp.Core.Models;

namespace Ubp.Infrastructure.Repositories;

public sealed partial class TreasuryRepository
{
    public async Task<ShiftOpeningBalances> GetShiftOpeningBalancesAsync(Guid branchId,
        CancellationToken ct = default)
    {
        await using var connection = await _factory.OpenAsync(ct);
        return await GetShiftOpeningBalancesAsync(connection, null, branchId, ct);
    }

    /// <summary>
    /// أرصدة فتح الوردية (محسوبة وغير قابلة للتعديل): كل خزينة بها طريقة دفع معلَّمة
    /// «يتم متابعتها في الوردية» مع رصيدها من الحسابات المرحّلة + الحركات غير المرحّلة
    /// (تحت المراجعة)، مع كل عدادات أجهزة الفرع (كل جهاز في صف حتى لو صفر) — 0075.
    /// </summary>
    private static async Task<ShiftOpeningBalances> GetShiftOpeningBalancesAsync(
        NpgsqlConnection connection, NpgsqlTransaction? tx, Guid branchId, CancellationToken ct)
    {
        var treasuries = (await connection.QueryAsync<ShiftTreasuryRow>(new CommandDefinition("""
            SELECT t.id AS TreasuryId, t.code AS Code, t.name_ar AS NameAr,
                   t.treasury_type AS TreasuryType,
                   COALESCE(prev.closing, gl.balance, 0) AS PostedBalance,
                   0::numeric AS UnpostedBalance
            FROM treasuries t
            -- أول المدة = آخر مدة مكتوبة (النقد المعدود) في الوردية السابقة لنفس الخزينة
            LEFT JOIN LATERAL (
                SELECT c2.counted_cash AS closing
                FROM shift_cash_counts c2
                JOIN cashier_shifts s2 ON s2.id = c2.shift_id
                WHERE c2.treasury_id = t.id AND s2.status = 'closed'
                  AND c2.counted_cash IS NOT NULL
                ORDER BY s2.closed_at DESC NULLS LAST
                LIMIT 1
            ) prev ON true
            -- احتياطي فقط عند عدم وجود وردية سابقة: رصيد حساب الأستاذ
            LEFT JOIN LATERAL (
                SELECT COALESCE((SELECT SUM(l.debit - l.credit)
                                 FROM journal_entry_lines l
                                 JOIN journal_entries e ON e.id = l.entry_id
                                                        AND e.branch_id = t.branch_id
                                 WHERE e.status = 'posted' AND l.account_id = t.account_id), 0)
                       AS balance
            ) gl ON true
            WHERE t.branch_id = @branchId AND t.is_active
              AND EXISTS (SELECT 1 FROM treasury_methods m2
                          WHERE m2.treasury_id = t.id AND m2.is_active AND m2.follow_in_shift)
            ORDER BY t.code
            """, new { branchId }, tx, cancellationToken: ct))).ToList();

        var methods = (await connection.QueryAsync<ShiftMethodRow>(new CommandDefinition("""
            SELECT m.id AS MethodId, m.code AS Code, m.name_ar AS NameAr,
                   m.method_type AS MethodType, m.follow_in_shift AS FollowInShift,
                   m.treasury_id AS TreasuryId
            FROM treasury_methods m
            JOIN treasuries t ON t.id = m.treasury_id
            WHERE t.branch_id = @branchId AND t.is_active AND m.is_active
              AND m.follow_in_shift
            ORDER BY m.treasury_id, m.code
            """, new { branchId }, tx, cancellationToken: ct))).ToList();

        var byTreasury = methods.GroupBy(m => m.TreasuryId)
            .ToDictionary(g => g.Key, g => g.ToList());
        foreach (var row in treasuries)
            if (byTreasury.TryGetValue(row.TreasuryId, out var list)) row.Methods = list;

        var meters = (await connection.QueryAsync<ShiftMeterRow>(new CommandDefinition("""
            SELECT id AS AssetId, code AS Code, name_ar AS NameAr,
                   meter_type AS MeterType, current_meter_value AS Value
            FROM fixed_assets
            WHERE branch_id = @branchId AND is_metered AND status <> 'DISPOSED'
            ORDER BY code
            """, new { branchId }, tx, cancellationToken: ct))).ToList();

        var carried = await connection.ExecuteScalarAsync<decimal>(new CommandDefinition("""
            SELECT COALESCE(SUM(l.debit - l.credit), 0)
            FROM journal_entry_lines l
            JOIN journal_entries e ON e.id = l.entry_id
            JOIN treasuries t ON t.account_id = l.account_id AND t.branch_id = e.branch_id
            WHERE e.status = 'posted' AND t.branch_id = @branchId
            """, new { branchId }, tx, cancellationToken: ct));

        var review = await connection.QuerySingleAsync<(decimal Total, int Count)>(new CommandDefinition("""
            SELECT COALESCE(SUM(i.amount), 0) AS Total,
                   count(DISTINCT i.shift_id)::int AS Count
            FROM shift_review_items i
            JOIN cashier_shifts s ON s.id = i.shift_id
            WHERE s.branch_id = @branchId AND s.review_status = 'pending'
              AND i.status = 'pending'
            """, new { branchId }, tx, cancellationToken: ct));

        return new ShiftOpeningBalances
        {
            BranchId = branchId,
            CarriedFromAccounts = Math.Round(carried, 2, MidpointRounding.AwayFromZero),
            PendingReview = Math.Round(review.Total, 2, MidpointRounding.AwayFromZero),
            PendingReviewShifts = review.Count,
            MeterBalance = Math.Round(meters.Sum(m => m.Value), 2, MidpointRounding.AwayFromZero),
            Treasuries = treasuries,
            Meters = meters,
            OpeningCash = Math.Round(treasuries.Sum(t => t.Total), 2,
                MidpointRounding.AwayFromZero)
        };
    }
}
