using Dapper;
using Npgsql;
using Ubp.Core.Models;
using Ubp.Core.Results;
using Ubp.Infrastructure.Data;

namespace Ubp.Infrastructure.Repositories;

public interface ISavedReportFilterRepository
{
    Task<IReadOnlyList<SavedReportFilter>> ListAsync(Guid userId, string reportKey,
        CancellationToken ct = default);
    Task<SavedReportFilter?> GetAsync(Guid id, CancellationToken ct = default);
    Task<SavedReportFilter> SaveAsync(Guid userId, string reportKey, string filterName,
        string filtersJson, CancellationToken ct = default);
    Task<bool> DeleteAsync(Guid id, Guid userId, CancellationToken ct = default);
}

/// <summary>
/// فلاتر التقارير المحفوظة: القيد الفريد (user_id, report_key, filter_name) يحوّل الحفظ
/// إلى Upsert بدل تكرار الصفوف، والمستخدم لا يرى أو يحذف إلا فلاتره.
/// </summary>
public sealed class SavedReportFilterRepository : ISavedReportFilterRepository
{
    private const string Columns = """
        id AS Id, user_id AS UserId, report_key AS ReportKey, filter_name AS FilterName,
        filters::text AS FiltersJson, created_at AS CreatedAt, updated_at AS UpdatedAt
        """;

    private readonly IDbConnectionFactory _factory;

    public SavedReportFilterRepository(IDbConnectionFactory factory) => _factory = factory;

    public async Task<IReadOnlyList<SavedReportFilter>> ListAsync(Guid userId, string reportKey,
        CancellationToken ct = default)
    {
        await using var connection = await _factory.OpenAsync(ct);
        var rows = await connection.QueryAsync<SavedReportFilter>(new CommandDefinition(
            $"SELECT {Columns} FROM saved_report_filters " +
            "WHERE user_id = @userId AND report_key = @reportKey ORDER BY lower(filter_name)",
            new { userId, reportKey }, cancellationToken: ct));
        return rows.ToList();
    }

    public async Task<SavedReportFilter?> GetAsync(Guid id, CancellationToken ct = default)
    {
        await using var connection = await _factory.OpenAsync(ct);
        return await connection.QuerySingleOrDefaultAsync<SavedReportFilter>(new CommandDefinition(
            $"SELECT {Columns} FROM saved_report_filters WHERE id = @id", new { id },
            cancellationToken: ct));
    }

    public async Task<SavedReportFilter> SaveAsync(Guid userId, string reportKey, string filterName,
        string filtersJson, CancellationToken ct = default)
    {
        const string sql = """
            INSERT INTO saved_report_filters (user_id, report_key, filter_name, filters)
            VALUES (@userId, @reportKey, @filterName, @filtersJson::jsonb)
            ON CONFLICT (user_id, report_key, filter_name)
            DO UPDATE SET filters = EXCLUDED.filters, updated_at = now()
            RETURNING id AS Id, user_id AS UserId, report_key AS ReportKey,
                      filter_name AS FilterName, filters::text AS FiltersJson,
                      created_at AS CreatedAt, updated_at AS UpdatedAt
            """;
        await using var connection = await _factory.OpenAsync(ct);
        try
        {
            return await connection.QuerySingleAsync<SavedReportFilter>(new CommandDefinition(sql,
                new { userId, reportKey, filterName, filtersJson }, cancellationToken: ct));
        }
        catch (PostgresException ex) when (ex.SqlState == "23514")
        {
            throw new ValidationAppException("validation.filter_name_invalid");
        }
        catch (PostgresException ex) when (ex.SqlState == "22P02")
        {
            throw new ValidationAppException("validation.filter_json_invalid");
        }
    }

    public async Task<bool> DeleteAsync(Guid id, Guid userId, CancellationToken ct = default)
    {
        await using var connection = await _factory.OpenAsync(ct);
        return await connection.ExecuteAsync(new CommandDefinition(
            "DELETE FROM saved_report_filters WHERE id = @id AND user_id = @userId",
            new { id, userId }, cancellationToken: ct)) > 0;
    }
}
