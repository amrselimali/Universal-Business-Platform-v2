using Dapper;
using Ubp.Core.Models;
using Ubp.Infrastructure.Data;

namespace Ubp.Infrastructure.Repositories;

public interface IImportJobRepository
{
    Task<Guid> CreateAsync(ImportJob job, CancellationToken ct = default);
    Task<IReadOnlyList<ImportJob>> ListAsync(int take = 20, CancellationToken ct = default);
    Task<ImportJob?> GetAsync(Guid id, CancellationToken ct = default);
}

/// <summary>سجل عمليات الاستيراد (قراءة + إضافة سجل واحد لكل تنفيذ).</summary>
public sealed class ImportJobRepository : IImportJobRepository
{
    private const string Columns = """
        id AS Id, kind AS Kind, file_name AS FileName, status AS Status,
        rows_total AS RowsTotal, rows_imported AS RowsImported, rows_failed AS RowsFailed,
        total_debit AS TotalDebit, total_credit AS TotalCredit, error_summary AS ErrorSummary,
        created_by AS CreatedBy, created_at AS CreatedAt, finished_at AS FinishedAt
        """;

    private readonly IDbConnectionFactory _factory;

    public ImportJobRepository(IDbConnectionFactory factory) => _factory = factory;

    public async Task<Guid> CreateAsync(ImportJob job, CancellationToken ct = default)
    {
        const string sql = """
            INSERT INTO import_jobs
                (kind, file_name, status, rows_total, rows_imported, rows_failed,
                 total_debit, total_credit, error_summary, created_by, finished_at)
            VALUES
                (@Kind, @FileName, @Status, @RowsTotal, @RowsImported, @RowsFailed,
                 @TotalDebit, @TotalCredit, @ErrorSummary, @CreatedBy, now())
            RETURNING id
            """;
        await using var connection = await _factory.OpenAsync(ct);
        return await connection.ExecuteScalarAsync<Guid>(new CommandDefinition(sql, job,
            cancellationToken: ct));
    }

    public async Task<IReadOnlyList<ImportJob>> ListAsync(int take = 20, CancellationToken ct = default)
    {
        await using var connection = await _factory.OpenAsync(ct);
        var rows = await connection.QueryAsync<ImportJob>(new CommandDefinition(
            $"SELECT {Columns} FROM import_jobs ORDER BY created_at DESC LIMIT @take",
            new { take = Math.Clamp(take, 1, 200) }, cancellationToken: ct));
        return rows.ToList();
    }

    public async Task<ImportJob?> GetAsync(Guid id, CancellationToken ct = default)
    {
        await using var connection = await _factory.OpenAsync(ct);
        return await connection.QuerySingleOrDefaultAsync<ImportJob>(new CommandDefinition(
            $"SELECT {Columns} FROM import_jobs WHERE id = @id", new { id }, cancellationToken: ct));
    }
}
