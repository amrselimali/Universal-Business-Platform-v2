using Dapper;
using Ubp.Core.Models;
using Ubp.Infrastructure.Data;

namespace Ubp.Infrastructure.Repositories;

public interface IQaRepository
{
    Task EnsureDefinitionsAsync(IEnumerable<QaTestDefinition> definitions, CancellationToken ct = default);
    Task<Guid> CreateRunAsync(Guid? userId, string environment, string? revision, string traceId,
        CancellationToken ct = default);
    Task AddResultAsync(Guid runId, QaTestResult result, CancellationToken ct = default);
    Task CompleteRunAsync(Guid runId, string status, int passed, int failed, CancellationToken ct = default);
    Task<IReadOnlyList<QaRunSummary>> ListRunsAsync(int limit = 20, CancellationToken ct = default);
    Task<IReadOnlyList<QaTestResult>> GetResultsAsync(Guid runId, CancellationToken ct = default);
    Task<IReadOnlyList<(string Code, string Status, DateTimeOffset At)>> GetLatestResultsAsync(
        CancellationToken ct = default);
}

public sealed class QaRepository : IQaRepository
{
    private readonly IDbConnectionFactory _factory;

    public QaRepository(IDbConnectionFactory factory) => _factory = factory;

    public async Task EnsureDefinitionsAsync(IEnumerable<QaTestDefinition> definitions,
        CancellationToken ct = default)
    {
        const string sql = """
            INSERT INTO qa_test_definitions (code, name_ar, name_en, category, criticality)
            VALUES (@Code, @NameAr, @NameEn, @Category, @Criticality)
            ON CONFLICT (code) DO UPDATE SET
                name_ar = EXCLUDED.name_ar, name_en = EXCLUDED.name_en,
                category = EXCLUDED.category, criticality = EXCLUDED.criticality
            """;
        await using var connection = await _factory.OpenAsync(ct);
        foreach (var definition in definitions)
            await connection.ExecuteAsync(new CommandDefinition(sql, definition, cancellationToken: ct));
    }

    public async Task<Guid> CreateRunAsync(Guid? userId, string environment, string? revision,
        string traceId, CancellationToken ct = default)
    {
        const string sql = """
            INSERT INTO qa_test_runs (requested_by, environment, code_revision, status, trace_id)
            VALUES (@userId, @environment, @revision, 'Running', @traceId)
            RETURNING id
            """;
        await using var connection = await _factory.OpenAsync(ct);
        return await connection.ExecuteScalarAsync<Guid>(new CommandDefinition(sql,
            new { userId, environment, revision, traceId }, cancellationToken: ct));
    }

    public async Task AddResultAsync(Guid runId, QaTestResult result, CancellationToken ct = default)
    {
        const string sql = """
            INSERT INTO qa_test_results (run_id, test_code, status, duration_ms, message, trace_id)
            VALUES (@runId, @TestCode, @Status, @DurationMs, @Message, @TraceId)
            """;
        await using var connection = await _factory.OpenAsync(ct);
        await connection.ExecuteAsync(new CommandDefinition(sql, new
        {
            runId, result.TestCode, result.Status, result.DurationMs, result.Message, result.TraceId
        }, cancellationToken: ct));
    }

    public async Task CompleteRunAsync(Guid runId, string status, int passed, int failed,
        CancellationToken ct = default)
    {
        const string sql = """
            UPDATE qa_test_runs SET status = @status, passed_count = @passed,
                failed_count = @failed, total_count = @passed + @failed, finished_at = now()
            WHERE id = @runId
            """;
        await using var connection = await _factory.OpenAsync(ct);
        await connection.ExecuteAsync(new CommandDefinition(sql, new { runId, status, passed, failed },
            cancellationToken: ct));
    }

    public async Task<IReadOnlyList<QaRunSummary>> ListRunsAsync(int limit = 20,
        CancellationToken ct = default)
    {
        const string sql = """
            SELECT id AS Id, environment AS Environment, code_revision AS CodeRevision,
                   status AS Status, total_count AS TotalCount, passed_count AS PassedCount,
                   failed_count AS FailedCount, trace_id AS TraceId, started_at AS StartedAt,
                   finished_at AS FinishedAt
            FROM qa_test_runs ORDER BY started_at DESC LIMIT @limit
            """;
        await using var connection = await _factory.OpenAsync(ct);
        var rows = await connection.QueryAsync<QaRunSummary>(
            new CommandDefinition(sql, new { limit }, cancellationToken: ct));
        return rows.ToList();
    }

    public async Task<IReadOnlyList<QaTestResult>> GetResultsAsync(Guid runId,
        CancellationToken ct = default)
    {
        const string sql = """
            SELECT test_code AS TestCode, status AS Status, duration_ms AS DurationMs,
                   message AS Message, trace_id AS TraceId
            FROM qa_test_results WHERE run_id = @runId ORDER BY id
            """;
        await using var connection = await _factory.OpenAsync(ct);
        var rows = await connection.QueryAsync<QaTestResult>(
            new CommandDefinition(sql, new { runId }, cancellationToken: ct));
        return rows.ToList();
    }

    public async Task<IReadOnlyList<(string Code, string Status, DateTimeOffset At)>> GetLatestResultsAsync(
        CancellationToken ct = default)
    {
        const string sql = """
            SELECT DISTINCT ON (test_code) test_code AS Code, status AS Status, created_at AS At
            FROM qa_test_results ORDER BY test_code, created_at DESC
            """;
        await using var connection = await _factory.OpenAsync(ct);
        var rows = await connection.QueryAsync<LatestRow>(
            new CommandDefinition(sql, cancellationToken: ct));
        return rows.Select(r => (r.Code, r.Status, r.At)).ToList();
    }

    private sealed class LatestRow
    {
        public string Code { get; set; } = "";
        public string Status { get; set; } = "";
        public DateTimeOffset At { get; set; }
    }
}
