using Dapper;
using Npgsql;
using Ubp.Core.Models;
using Ubp.Infrastructure.Data;

namespace Ubp.Infrastructure.Repositories;

public interface IBackupLogRepository
{
    Task<IReadOnlyList<BackupLogEntry>> ListAsync(int take = 20, CancellationToken ct = default);
    Task<BackupLogEntry?> LastAsync(CancellationToken ct = default);
}

/// <summary>قراءة سجل النسخ الاحتياطي فقط — الإدخال من وظيفة cron الخارجية.</summary>
public sealed class BackupLogRepository : IBackupLogRepository
{
    private const string Columns = """
        id AS Id, started_at AS StartedAt, finished_at AS FinishedAt, status AS Status,
        kind AS Kind, file_name AS FileName, size_bytes AS SizeBytes,
        duration_seconds AS DurationSeconds, error AS Error
        """;

    private readonly IDbConnectionFactory _factory;

    public BackupLogRepository(IDbConnectionFactory factory) => _factory = factory;

    public async Task<IReadOnlyList<BackupLogEntry>> ListAsync(int take = 20,
        CancellationToken ct = default)
    {
        await using var connection = await _factory.OpenAsync(ct);
        var rows = await connection.QueryAsync<BackupLogEntry>(new CommandDefinition(
            $"SELECT {Columns} FROM backup_log ORDER BY started_at DESC LIMIT @take",
            new { take = Math.Clamp(take, 1, 200) }, cancellationToken: ct));
        return rows.ToList();
    }

    public async Task<BackupLogEntry?> LastAsync(CancellationToken ct = default)
    {
        await using var connection = await _factory.OpenAsync(ct);
        return await connection.QueryFirstOrDefaultAsync<BackupLogEntry>(new CommandDefinition(
            $"SELECT {Columns} FROM backup_log ORDER BY started_at DESC LIMIT 1",
            cancellationToken: ct));
    }
}

public interface ISystemHealthRepository
{
    Task<SystemHealthFacts> GetFactsAsync(int longRunningSeconds = 30,
        CancellationToken ct = default);
}

/// <summary>حقائق صحية مباشرة من PostgreSQL (إصدار، حجم، جداول، استعلامات طويلة، آخر نسخة).</summary>
public sealed class SystemHealthRepository : ISystemHealthRepository
{
    private readonly IDbConnectionFactory _factory;

    public SystemHealthRepository(IDbConnectionFactory factory) => _factory = factory;

    public async Task<SystemHealthFacts> GetFactsAsync(int longRunningSeconds = 30,
        CancellationToken ct = default)
    {
        await using var connection = await _factory.OpenAsync(ct);
        try
        {
            const string sql = """
                SELECT current_setting('server_version')                       AS Version,
                       current_database()                                      AS DatabaseName,
                       current_user                                            AS UserName,
                       pg_database_size(current_database())::bigint            AS SizeBytes,
                       (SELECT count(*)::int FROM information_schema.tables
                        WHERE table_schema NOT IN ('pg_catalog', 'information_schema')) AS TableCount,
                       (SELECT count(*)::int FROM pg_stat_activity
                        WHERE state = 'active' AND pid <> pg_backend_pid()
                          AND now() - query_start > make_interval(secs => @seconds))
                                                                               AS LongRunning
                """;
            var row = await connection.QuerySingleAsync<FactsRow>(new CommandDefinition(sql,
                new { seconds = Math.Clamp(longRunningSeconds, 1, 3600) }, cancellationToken: ct));

            var lastBackup = await new BackupLogRepository(_factory).LastAsync(ct);
            return new SystemHealthFacts(true, row.Version, row.DatabaseName, row.UserName, null,
                row.SizeBytes, row.TableCount, row.LongRunning, lastBackup);
        }
        catch (PostgresException ex)
        {
            return new SystemHealthFacts(false, null, null, null, ex.Message, 0, 0, 0, null);
        }
        catch (NpgsqlException ex)
        {
            return new SystemHealthFacts(false, null, null, null, ex.Message, 0, 0, 0, null);
        }
    }

    private sealed class FactsRow
    {
        public string? Version { get; set; }
        public string? DatabaseName { get; set; }
        public string? UserName { get; set; }
        public long SizeBytes { get; set; }
        public int TableCount { get; set; }
        public int LongRunning { get; set; }
    }
}
