using Dapper;
using Ubp.Core.Models;
using Ubp.Core.Paging;
using Ubp.Infrastructure.Data;

namespace Ubp.Infrastructure.Repositories;

public interface IPeriodRepository
{
    Task<PagedResult<FiscalPeriod>> ListAsync(PageRequest request, string? status,
        CancellationToken ct = default);
    Task<IReadOnlyList<FiscalPeriod>> ListOptionsAsync(CancellationToken ct = default);
    Task<FiscalPeriod?> GetAsync(Guid id, CancellationToken ct = default);
    Task<FiscalPeriod> CreateAsync(FiscalPeriodInput input, CancellationToken ct = default);
    Task<FiscalPeriod?> UpdateAsync(Guid id, int version, FiscalPeriodInput input, CancellationToken ct = default);
    Task<bool> DeleteAsync(Guid id, CancellationToken ct = default);
    /// <summary>إغلاق الفترة يُرفض ذرياً إن وُجدت مستندات مخزنية غير مرحلة.</summary>
    Task<bool> SetStatusAsync(Guid id, string status, Guid userId, string? reason, CancellationToken ct = default);

    Task<IReadOnlyList<PeriodClosure>> ListClosuresAsync(Guid periodId, CancellationToken ct = default);
    Task<PeriodClosure?> GetClosureAsync(Guid periodId, string scopeType, Guid? branchId,
        string moduleCode, CancellationToken ct = default);
    /// <summary>عند الإغلاق يتحقق داخل نفس الجملة من عدم وجود مسودات في النطاق (بوابة تزامن).</summary>
    Task<bool> UpsertClosureAsync(Guid periodId, string scopeType, Guid? branchId, string moduleCode,
        bool closed, Guid userId, string? reason, CancellationToken ct = default);
    Task<int> CountOpenClosuresAsync(Guid periodId, CancellationToken ct = default);
    Task<int> CountClosuresAsync(Guid periodId, CancellationToken ct = default);
    Task<int> CountOverlappingAsync(DateTime from, DateTime to, Guid? excludeId,
        CancellationToken ct = default);
    Task<int> CountBranchesWithoutClosureAsync(Guid periodId, string moduleCode,
        CancellationToken ct = default);
    Task<int> CountDraftDocumentsAsync(DateTime from, DateTime to, Guid? branchId,
        CancellationToken ct = default);
    Task<int> CountUnbalancedBalancesAsync(CancellationToken ct = default);
    Task<int> CountUnfinishedTransfersAsync(DateTime from, DateTime to, Guid? branchId,
        CancellationToken ct = default);
    Task<int> CountDraftStocktakesAsync(DateTime from, DateTime to, Guid? branchId,
        CancellationToken ct = default);
    Task<int> CountZeroCostBalancesAsync(Guid? branchId, CancellationToken ct = default);

    Task AddCheckAsync(PeriodCheckInput input, Guid userId, CancellationToken ct = default);
    Task<IReadOnlyList<PeriodCheck>> ListChecksAsync(Guid periodId, CancellationToken ct = default);
    Task AddEventAsync(Guid periodId, string scopeType, Guid? branchId, string moduleCode,
        string actionCode, Guid userId, string? reason, string? traceId,
        CancellationToken ct = default);
    Task<IReadOnlyList<PeriodEvent>> ListEventsAsync(Guid periodId, CancellationToken ct = default);
}

public sealed partial class PeriodRepository : IPeriodRepository
{
    private const string Columns = """
        id AS Id, code AS Code, name_ar AS NameAr, name_en AS NameEn,
        start_date AS StartDate, end_date AS EndDate, status AS Status,
        closed_at AS ClosedAt, reopened_at AS ReopenedAt, reopen_reason AS ReopenReason,
        created_at AS CreatedAt, updated_at AS UpdatedAt, version AS Version
        """;

    private static readonly IReadOnlyDictionary<string, string> SortMap =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["code"] = "code", ["start_date"] = "start_date",
            ["end_date"] = "end_date", ["status"] = "status"
        };

    private readonly IDbConnectionFactory _factory;

    public PeriodRepository(IDbConnectionFactory factory) => _factory = factory;

    public async Task<PagedResult<FiscalPeriod>> ListAsync(PageRequest request, string? status,
        CancellationToken ct = default)
    {
        var where = new List<string>();
        if (!string.IsNullOrWhiteSpace(status)) where.Add("status = @status");
        if (!string.IsNullOrWhiteSpace(request.Search))
            where.Add("normalize_search(code || ' ' || name_ar || ' ' || coalesce(name_en,'')) LIKE '%' || normalize_search(@search) || '%'");
        var whereSql = where.Count == 0 ? "WHERE true" : "WHERE " + string.Join(" AND ", where);
        var order = SqlSort.Build(request.SortBy, SortMap, "start_date", request.Descending);
        var sql = $"""
            SELECT {Columns} FROM fiscal_periods {whereSql}
            ORDER BY {order} LIMIT @take OFFSET @offset;
            SELECT count(*) FROM fiscal_periods {whereSql};
            """;

        await using var connection = await _factory.OpenAsync(ct);
        using var multi = await connection.QueryMultipleAsync(new CommandDefinition(sql, new
        {
            search = request.Search, status, take = request.SafePageSize, offset = request.Offset
        }, cancellationToken: ct));
        var items = (await multi.ReadAsync<FiscalPeriod>()).ToList();
        var total = await multi.ReadFirstAsync<int>();
        return new PagedResult<FiscalPeriod>
        {
            Items = items, Total = total, Page = request.SafePage, PageSize = request.SafePageSize
        };
    }

    public async Task<IReadOnlyList<FiscalPeriod>> ListOptionsAsync(CancellationToken ct = default)
    {
        await using var connection = await _factory.OpenAsync(ct);
        var rows = await connection.QueryAsync<FiscalPeriod>(new CommandDefinition(
            $"SELECT {Columns} FROM fiscal_periods ORDER BY start_date DESC", cancellationToken: ct));
        return rows.ToList();
    }

    public async Task<FiscalPeriod?> GetAsync(Guid id, CancellationToken ct = default)
    {
        await using var connection = await _factory.OpenAsync(ct);
        return await connection.QuerySingleOrDefaultAsync<FiscalPeriod>(new CommandDefinition(
            $"SELECT {Columns} FROM fiscal_periods WHERE id = @id", new { id }, cancellationToken: ct));
    }

    public async Task<PeriodClosure?> GetClosureAsync(Guid periodId, string scopeType, Guid? branchId,
        string moduleCode, CancellationToken ct = default)
    {
        const string sql = """
            SELECT id AS Id, period_id AS PeriodId, scope_type AS ScopeType,
                   branch_id AS BranchId,
                   (SELECT b.name_ar FROM branches b WHERE b.id = c.branch_id) AS BranchName,
                   module_code AS ModuleCode, status AS Status, closed_at AS ClosedAt,
                   (SELECT u.username FROM users u WHERE u.id = c.closed_by) AS ClosedByName,
                   reopened_at AS ReopenedAt, reopen_reason AS ReopenReason
            FROM period_scope_closures c
            WHERE period_id = @periodId AND scope_type = @scopeType
              AND branch_id IS NOT DISTINCT FROM @branchId AND module_code = @moduleCode
            """;
        await using var connection = await _factory.OpenAsync(ct);
        return await connection.QuerySingleOrDefaultAsync<PeriodClosure>(new CommandDefinition(
            sql, new { periodId, scopeType, branchId, moduleCode }, cancellationToken: ct));
    }

    public async Task<IReadOnlyList<PeriodClosure>> ListClosuresAsync(Guid periodId,
        CancellationToken ct = default)
    {
        const string sql = """
            SELECT c.id AS Id, c.period_id AS PeriodId, c.scope_type AS ScopeType,
                   c.branch_id AS BranchId,
                   (SELECT b.name_ar FROM branches b WHERE b.id = c.branch_id) AS BranchName,
                   c.module_code AS ModuleCode, c.status AS Status, c.closed_at AS ClosedAt,
                   (SELECT u.username FROM users u WHERE u.id = c.closed_by) AS ClosedByName,
                   c.reopened_at AS ReopenedAt, c.reopen_reason AS ReopenReason
            FROM period_scope_closures c
            WHERE c.period_id = @periodId
            ORDER BY c.module_code, c.scope_type DESC, c.branch_id
            """;
        await using var connection = await _factory.OpenAsync(ct);
        var rows = await connection.QueryAsync<PeriodClosure>(new CommandDefinition(sql, new { periodId }, cancellationToken: ct));
        return rows.ToList();
    }

    public async Task<int> CountOpenClosuresAsync(Guid periodId, CancellationToken ct = default)
    {
        await using var connection = await _factory.OpenAsync(ct);
        return await connection.ExecuteScalarAsync<int>(new CommandDefinition(
            "SELECT count(*)::int FROM period_scope_closures WHERE period_id = @periodId AND status = 'OPEN'",
            new { periodId }, cancellationToken: ct));
    }

    public async Task<int> CountClosuresAsync(Guid periodId, CancellationToken ct = default)
    {
        await using var connection = await _factory.OpenAsync(ct);
        return await connection.ExecuteScalarAsync<int>(new CommandDefinition(
            "SELECT count(*)::int FROM period_scope_closures WHERE period_id = @periodId",
            new { periodId }, cancellationToken: ct));
    }

    public async Task<int> CountOverlappingAsync(DateTime from, DateTime to, Guid? excludeId,
        CancellationToken ct = default)
    {
        const string sql = """
            SELECT count(*)::int FROM fiscal_periods
            WHERE start_date <= @to AND end_date >= @from
              AND (@excludeId IS NULL OR id <> @excludeId)
            """;
        await using var connection = await _factory.OpenAsync(ct);
        return await connection.ExecuteScalarAsync<int>(new CommandDefinition(sql,
            new { from, to, excludeId }, cancellationToken: ct));
    }

    public async Task<int> CountUnfinishedTransfersAsync(DateTime from, DateTime to, Guid? branchId,
        CancellationToken ct = default)
    {
        const string sql = """
            SELECT CASE WHEN to_regclass('public.stock_documents') IS NULL THEN 0 ELSE (
                SELECT count(*)::int FROM stock_documents d
                WHERE d.doc_type = 'TRANSFER'
                  AND d.status IN ('POSTED','PARTIALLY_RECEIVED')
                  AND d.doc_date BETWEEN @from AND @to
                  AND (@branchId IS NULL OR d.branch_id = @branchId OR d.to_branch_id = @branchId)) END
            """;
        await using var connection = await _factory.OpenAsync(ct);
        return await connection.ExecuteScalarAsync<int>(new CommandDefinition(sql,
            new { from, to, branchId }, cancellationToken: ct));
    }

    public async Task<int> CountDraftStocktakesAsync(DateTime from, DateTime to, Guid? branchId,
        CancellationToken ct = default)
    {
        const string sql = """
            SELECT CASE WHEN to_regclass('public.stocktakes') IS NULL THEN 0 ELSE (
                SELECT count(*)::int FROM stocktakes s
                WHERE s.status = 'DRAFT'
                  AND s.created_at::date BETWEEN @from AND @to
                  AND (@branchId IS NULL OR s.branch_id = @branchId)) END
            """;
        await using var connection = await _factory.OpenAsync(ct);
        return await connection.ExecuteScalarAsync<int>(new CommandDefinition(sql,
            new { from, to, branchId }, cancellationToken: ct));
    }

    public async Task<int> CountZeroCostBalancesAsync(Guid? branchId, CancellationToken ct = default)
    {
        const string sql = """
            SELECT CASE WHEN to_regclass('public.stock_balances') IS NULL THEN 0 ELSE (
                SELECT count(*)::int FROM stock_balances b
                WHERE b.on_hand > 0 AND b.average_cost = 0
                  AND (@branchId IS NULL OR EXISTS (
                      SELECT 1 FROM warehouse_branches wb
                      WHERE wb.warehouse_id = b.warehouse_id AND wb.branch_id = @branchId))) END
            """;
        await using var connection = await _factory.OpenAsync(ct);
        return await connection.ExecuteScalarAsync<int>(new CommandDefinition(sql,
            new { branchId }, cancellationToken: ct));
    }

    public async Task<int> CountBranchesWithoutClosureAsync(Guid periodId, string moduleCode,
        CancellationToken ct = default)
    {
        const string sql = """
            SELECT count(*)::int FROM branches b
            WHERE b.is_active AND NOT EXISTS (
                SELECT 1 FROM period_scope_closures c
                WHERE c.period_id = @periodId AND c.module_code = @moduleCode
                  AND c.scope_type = 'BRANCH' AND c.branch_id = b.id AND c.status = 'CLOSED')
            """;
        await using var connection = await _factory.OpenAsync(ct);
        return await connection.ExecuteScalarAsync<int>(new CommandDefinition(sql,
            new { periodId, moduleCode }, cancellationToken: ct));
    }

    public async Task<int> CountDraftDocumentsAsync(DateTime from, DateTime to, Guid? branchId,
        CancellationToken ct = default)
    {
        const string sql = """
            SELECT CASE WHEN to_regclass('public.stock_documents') IS NULL THEN 0 ELSE (
                SELECT count(*)::int FROM stock_documents d
                WHERE d.status = 'DRAFT' AND d.doc_date BETWEEN @from AND @to
                  AND (@branchId IS NULL OR d.branch_id = @branchId)) END
            """;
        await using var connection = await _factory.OpenAsync(ct);
        return await connection.ExecuteScalarAsync<int>(new CommandDefinition(sql,
            new { from, to, branchId }, cancellationToken: ct));
    }

    public async Task<int> CountUnbalancedBalancesAsync(CancellationToken ct = default)
    {
        const string sql = """
            SELECT CASE WHEN to_regclass('public.stock_movements') IS NULL THEN 0 ELSE (
                SELECT count(*)::int FROM stock_balances b
                WHERE b.on_hand <> COALESCE((
                    SELECT sum(CASE WHEN m.direction = 'IN' THEN m.quantity ELSE -m.quantity END)
                    FROM stock_movements m
                    WHERE m.item_id = b.item_id AND m.warehouse_id = b.warehouse_id), 0)) END
            """;
        await using var connection = await _factory.OpenAsync(ct);
        return await connection.ExecuteScalarAsync<int>(new CommandDefinition(sql, cancellationToken: ct));
    }

    public async Task<IReadOnlyList<PeriodCheck>> ListChecksAsync(Guid periodId,
        CancellationToken ct = default)
    {
        const string sql = """
            SELECT id AS Id, period_id AS PeriodId, scope_type AS ScopeType, branch_id AS BranchId,
                   module_code AS ModuleCode, check_code AS CheckCode, status AS Status,
                   message AS Message, checked_at AS CheckedAt
            FROM period_close_checks WHERE period_id = @periodId
            ORDER BY checked_at DESC LIMIT 100
            """;
        await using var connection = await _factory.OpenAsync(ct);
        var rows = await connection.QueryAsync<PeriodCheck>(new CommandDefinition(sql, new { periodId }, cancellationToken: ct));
        return rows.ToList();
    }

    public async Task<IReadOnlyList<PeriodEvent>> ListEventsAsync(Guid periodId,
        CancellationToken ct = default)
    {
        const string sql = """
            SELECT e.id AS Id, e.period_id AS PeriodId, e.scope_type AS ScopeType,
                   e.branch_id AS BranchId, e.module_code AS ModuleCode, e.action_code AS ActionCode,
                   (SELECT u.username FROM users u WHERE u.id = e.actor_user_id) AS ActorName,
                   e.event_at AS EventAt, e.reason AS Reason, e.trace_id AS TraceId
            FROM period_events e WHERE e.period_id = @periodId
            ORDER BY e.event_at DESC LIMIT 100
            """;
        await using var connection = await _factory.OpenAsync(ct);
        var rows = await connection.QueryAsync<PeriodEvent>(new CommandDefinition(sql, new { periodId }, cancellationToken: ct));
        return rows.ToList();
    }
}
