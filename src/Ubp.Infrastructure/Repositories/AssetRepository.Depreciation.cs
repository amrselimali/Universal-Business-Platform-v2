using Dapper;
using Ubp.Core.Models;
using Ubp.Infrastructure.Data;

namespace Ubp.Infrastructure.Repositories;

/// <summary>أصل قابل للإهلاك مع بيانات تصنيفه ورصيد الإهلاك المتراكم.</summary>
public sealed record DepreciableAsset(Guid AssetId, Guid BranchId, decimal Cost, decimal SalvageValue,
    string Method, string Frequency, int UsefulLifeMonths, Guid ExpenseAccountId,
    Guid AccumAccountId, decimal Accumulated, decimal? ExpectedUnits);

public sealed partial class AssetRepository
{
    public async Task<IReadOnlyList<DepreciableAsset>> ListDepreciableAsync(Guid branchId,
        DateOnly monthEnd, CancellationToken ct = default)
    {
        const string sql = """
            SELECT a.id AS AssetId, a.branch_id AS BranchId, a.cost AS Cost,
                   a.salvage_value AS SalvageValue, c.depreciation_method AS Method,
                   c.depreciation_frequency AS Frequency,
                   c.useful_life_months AS UsefulLifeMonths,
                   c.expense_account_id AS ExpenseAccountId,
                   c.accum_account_id AS AccumAccountId,
                   COALESCE((SELECT SUM(l.amount) FROM asset_depreciation_lines l
                             WHERE l.asset_id = a.id), 0) AS Accumulated,
                   c.expected_units AS ExpectedUnits
            FROM fixed_assets a
            JOIN asset_categories c ON c.id = a.category_id
            WHERE a.status = 'ACTIVE' AND c.is_active
              AND a.branch_id = @branchId
              AND (a.in_service_date IS NULL OR a.in_service_date <= @monthEnd)
              AND c.expense_account_id IS NOT NULL AND c.accum_account_id IS NOT NULL
            ORDER BY a.code
            """;
        await using var connection = await _factory.OpenAsync(ct);
        var rows = await connection.QueryAsync<DepreciableAsset>(new CommandDefinition(sql,
            new { branchId, monthEnd }, cancellationToken: ct));
        return rows.ToList();
    }

    public async Task<decimal> MeterDeltaAsync(Guid assetId, DateOnly from, DateOnly to,
        CancellationToken ct = default)
    {
        await using var connection = await _factory.OpenAsync(ct);
        return await connection.ExecuteScalarAsync<decimal>(new CommandDefinition("""
            SELECT COALESCE(SUM(delta), 0) FROM asset_meter_readings
            WHERE asset_id = @assetId AND source <> 'MAINTENANCE_RESET'
              AND reading_at >= @from AND reading_at < @to
            """, new { assetId, from = from.ToDateTime(TimeOnly.MinValue),
                to = to.ToDateTime(TimeOnly.MinValue) }, cancellationToken: ct));
    }

    public async Task<bool> RunExistsAsync(DateOnly periodMonth, Guid branchId,
        CancellationToken ct = default)
    {
        await using var connection = await _factory.OpenAsync(ct);
        return await connection.ExecuteScalarAsync<bool>(new CommandDefinition("""
            SELECT EXISTS (SELECT 1 FROM asset_depreciation_runs
                           WHERE period_month = @periodMonth AND branch_id = @branchId)
            """, new { periodMonth, branchId }, cancellationToken: ct));
    }

    public async Task<Guid> InsertRunAsync(DateOnly periodMonth, Guid branchId, decimal total,
        Guid? journalEntryId, Guid userId, CancellationToken ct = default)
    {
        await using var connection = await _factory.OpenAsync(ct);
        return await connection.ExecuteScalarAsync<Guid>(new CommandDefinition("""
            INSERT INTO asset_depreciation_runs
                (period_month, branch_id, total_amount, journal_entry_id, run_by)
            VALUES (@periodMonth, @branchId, @total, @journalEntryId, @userId)
            RETURNING id
            """, new { periodMonth, branchId, total, journalEntryId, userId },
            cancellationToken: ct));
    }

    public async Task InsertLineAsync(Guid runId, Guid assetId, DateOnly periodMonth,
        decimal amount, decimal accumulatedAfter, CancellationToken ct = default)
    {
        await using var connection = await _factory.OpenAsync(ct);
        await connection.ExecuteAsync(new CommandDefinition("""
            INSERT INTO asset_depreciation_lines
                (run_id, asset_id, period_month, amount, accumulated_after)
            VALUES (@runId, @assetId, @periodMonth, @amount, @accumulatedAfter)
            ON CONFLICT (asset_id, period_month) DO NOTHING
            """, new { runId, assetId, periodMonth, amount, accumulatedAfter }, cancellationToken: ct));
    }
}
