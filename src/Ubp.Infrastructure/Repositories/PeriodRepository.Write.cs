using Dapper;
using Ubp.Core.Models;

namespace Ubp.Infrastructure.Repositories;

public sealed partial class PeriodRepository
{
    public async Task<FiscalPeriod> CreateAsync(FiscalPeriodInput input, CancellationToken ct = default)
    {
        const string sql = """
            INSERT INTO fiscal_periods (code, name_ar, name_en, start_date, end_date)
            VALUES (@Code, @NameAr, @NameEn, @StartDate, @EndDate)
            RETURNING id
            """;
        await using var connection = await _factory.OpenAsync(ct);
        var id = await connection.ExecuteScalarAsync<Guid>(new CommandDefinition(sql, input, cancellationToken: ct));
        return (await GetAsync(id, ct))!;
    }

    public async Task<FiscalPeriod?> UpdateAsync(Guid id, int version, FiscalPeriodInput input,
        CancellationToken ct = default)
    {
        const string sql = """
            UPDATE fiscal_periods SET code = @Code, name_ar = @NameAr, name_en = @NameEn,
                                     start_date = @StartDate, end_date = @EndDate
            WHERE id = @id AND version = @version AND status = 'OPEN'
            RETURNING id
            """;
        await using var connection = await _factory.OpenAsync(ct);
        var updated = await connection.ExecuteScalarAsync<Guid?>(new CommandDefinition(sql, new
        {
            id, version, input.Code, input.NameAr, input.NameEn, input.StartDate, input.EndDate
        }, cancellationToken: ct));
        return updated is null ? null : await GetAsync(id, ct);
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        await using var connection = await _factory.OpenAsync(ct);
        return await connection.ExecuteAsync(new CommandDefinition(
            "DELETE FROM fiscal_periods WHERE id = @id", new { id }, cancellationToken: ct)) > 0;
    }

    public async Task SetStatusAsync(Guid id, string status, Guid userId, string? reason,
        CancellationToken ct = default)
    {
        const string sql = """
            UPDATE fiscal_periods SET
                status = @status,
                closed_at = CASE WHEN @status = 'CLOSED' THEN now() ELSE closed_at END,
                closed_by = CASE WHEN @status = 'CLOSED' THEN @userId ELSE closed_by END,
                reopened_at = CASE WHEN @status = 'OPEN' THEN now() ELSE reopened_at END,
                reopened_by = CASE WHEN @status = 'OPEN' THEN @userId ELSE reopened_by END,
                reopen_reason = CASE WHEN @status = 'OPEN' THEN @reason ELSE reopen_reason END
            WHERE id = @id
            """;
        await using var connection = await _factory.OpenAsync(ct);
        await connection.ExecuteAsync(new CommandDefinition(sql,
            new { id, status, userId, reason }, cancellationToken: ct));
    }

    public async Task UpsertClosureAsync(Guid periodId, string scopeType, Guid? branchId,
        string moduleCode, bool closed, Guid userId, string? reason, CancellationToken ct = default)
    {
        const string sql = """
            INSERT INTO period_scope_closures
                (period_id, scope_type, branch_id, module_code, status, closed_at, closed_by)
            VALUES
                (@periodId, @scopeType, @branchId, @moduleCode, @status, now(), @userId)
            ON CONFLICT ON CONSTRAINT ux_period_scope
            DO UPDATE SET
                status = EXCLUDED.status,
                closed_at = CASE WHEN EXCLUDED.status = 'CLOSED' THEN now() ELSE period_scope_closures.closed_at END,
                closed_by = CASE WHEN EXCLUDED.status = 'CLOSED' THEN @userId ELSE period_scope_closures.closed_by END,
                reopened_at = CASE WHEN EXCLUDED.status = 'OPEN' THEN now() ELSE period_scope_closures.reopened_at END,
                reopened_by = CASE WHEN EXCLUDED.status = 'OPEN' THEN @userId ELSE period_scope_closures.reopened_by END,
                reopen_reason = CASE WHEN EXCLUDED.status = 'OPEN' THEN @reason ELSE period_scope_closures.reopen_reason END
            """;
        await using var connection = await _factory.OpenAsync(ct);
        await connection.ExecuteAsync(new CommandDefinition(sql, new
        {
            periodId, scopeType, branchId, moduleCode,
            status = closed ? "CLOSED" : "OPEN", userId, reason
        }, cancellationToken: ct));
    }

    public async Task AddCheckAsync(PeriodCheckInput input, Guid userId, CancellationToken ct = default)
    {
        const string sql = """
            INSERT INTO period_close_checks
                (period_id, scope_type, branch_id, module_code, check_code, status, message,
                 details, checked_by)
            VALUES
                (@PeriodId, @ScopeType, @BranchId, @ModuleCode, @CheckCode, @Status, @Message,
                 CAST(@Details AS jsonb), @userId)
            """;
        await using var connection = await _factory.OpenAsync(ct);
        await connection.ExecuteAsync(new CommandDefinition(sql, new
        {
            input.PeriodId, input.ScopeType, input.BranchId, input.ModuleCode, input.CheckCode,
            input.Status, input.Message, input.Details, userId
        }, cancellationToken: ct));
    }

    public async Task AddEventAsync(Guid periodId, string scopeType, Guid? branchId,
        string moduleCode, string actionCode, Guid userId, string? reason, string? traceId,
        CancellationToken ct = default)
    {
        const string sql = """
            INSERT INTO period_events
                (period_id, scope_type, branch_id, module_code, action_code, actor_user_id,
                 reason, trace_id)
            VALUES
                (@periodId, @scopeType, @branchId, @moduleCode, @actionCode, @userId, @reason, @traceId)
            """;
        await using var connection = await _factory.OpenAsync(ct);
        await connection.ExecuteAsync(new CommandDefinition(sql, new
        {
            periodId, scopeType, branchId, moduleCode, actionCode, userId, reason, traceId
        }, cancellationToken: ct));
    }
}
