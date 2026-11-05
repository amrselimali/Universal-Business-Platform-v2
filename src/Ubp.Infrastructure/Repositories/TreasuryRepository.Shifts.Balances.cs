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
    /// أرصدة افتتاح الوردية (محسوبة، غير قابلة للتعديل):
    /// المرحّل من حسابات خزائن الفرع + الرصيد في الورديات المغلقة تحت المراجعة + عدادات الأجهزة.
    /// </summary>
    private static async Task<ShiftOpeningBalances> GetShiftOpeningBalancesAsync(
        NpgsqlConnection connection, NpgsqlTransaction? tx, Guid branchId, CancellationToken ct)
    {
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

        var meters = await connection.ExecuteScalarAsync<decimal>(new CommandDefinition("""
            SELECT COALESCE(SUM(current_meter_value), 0)
            FROM fixed_assets
            WHERE branch_id = @branchId AND is_metered AND status <> 'DISPOSED'
            """, new { branchId }, tx, cancellationToken: ct));

        return new ShiftOpeningBalances
        {
            BranchId = branchId,
            CarriedFromAccounts = Math.Round(carried, 2, MidpointRounding.AwayFromZero),
            PendingReview = Math.Round(review.Total, 2, MidpointRounding.AwayFromZero),
            PendingReviewShifts = review.Count,
            MeterBalance = Math.Round(meters, 2, MidpointRounding.AwayFromZero)
        };
    }
}

