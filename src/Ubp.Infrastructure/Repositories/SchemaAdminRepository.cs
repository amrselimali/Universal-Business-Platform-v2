using System.Text.Json;
using System.Text.RegularExpressions;
using Dapper;
using Npgsql;
using Ubp.Infrastructure.Data;

namespace Ubp.Infrastructure.Repositories;

public sealed class TableInfo
{
    public string Schema { get; set; } = "";
    public string Name { get; set; } = "";
    public long RowCount { get; set; }
    public int ColumnCount { get; set; }
}

public sealed record ColumnInfo(string Name, string DataType, bool Nullable);
public sealed record RowPage(IReadOnlyList<string> Columns, IReadOnlyList<string> RowsJson, int Total);

public interface ISchemaAdminRepository
{
    Task<(bool Ok, string? Server, string? Database, string? User, string? Error)> TestAsync(
        CancellationToken ct = default);
    Task<IReadOnlyList<TableInfo>> GetTablesAsync(CancellationToken ct = default);
    Task<IReadOnlyList<ColumnInfo>> GetColumnsAsync(string table, CancellationToken ct = default);
    Task<RowPage> GetRowsAsync(string table, int page, CancellationToken ct = default);
    Task<Dictionary<string, object?>?> GetRowAsync(string table, string id, CancellationToken ct = default);
    Task<bool> UpdateRowAsync(string table, string id, Dictionary<string, object?> values,
        CancellationToken ct = default);
    Task<bool> DeleteRowAsync(string table, string id, CancellationToken ct = default);
}

public sealed partial class SchemaAdminRepository : ISchemaAdminRepository
{
    private static readonly Regex SafeName = new("^[a-z_][a-z0-9_]*$", RegexOptions.Compiled);
    private static readonly HashSet<string> ProtectedColumns = new(StringComparer.OrdinalIgnoreCase)
    { "id", "password_hash", "token_hash", "search_text", "created_at", "updated_at", "version" };

    private readonly IDbConnectionFactory _factory;

    public SchemaAdminRepository(IDbConnectionFactory factory) => _factory = factory;

    public async Task<(bool Ok, string? Server, string? Database, string? User, string? Error)> TestAsync(
        CancellationToken ct = default)
    {
        try
        {
            await using var connection = await _factory.OpenAsync(ct);
            var value = await connection.ExecuteScalarAsync<string>(new CommandDefinition(
                "SELECT current_setting('server_version') || '|' || current_database() || '|' || current_user",
                cancellationToken: ct));
            var parts = value.Split('|');
            return (true, parts.ElementAtOrDefault(0), parts.ElementAtOrDefault(1), parts.ElementAtOrDefault(2), null);
        }
        catch (Exception ex)
        {
            return (false, null, null, null, ex.Message);
        }
    }

    public async Task<IReadOnlyList<TableInfo>> GetTablesAsync(CancellationToken ct = default)
    {
        const string sql = """
            SELECT n.nspname AS Schema, c.relname AS Name,
                   (SELECT count(*)::int FROM information_schema.columns col
                    WHERE col.table_schema = n.nspname AND col.table_name = c.relname) AS ColumnCount
            FROM pg_class c
            JOIN pg_namespace n ON n.oid = c.relnamespace
            WHERE c.relkind = 'r' AND n.nspname = 'public'
            ORDER BY c.relname
            """;
        await using var connection = await _factory.OpenAsync(ct);
        var rows = (await connection.QueryAsync<TableInfo>(new CommandDefinition(sql, cancellationToken: ct))).ToList();
        foreach (var row in rows)
        {
            if (!SafeName.IsMatch(row.Name)) continue;
            var count = await connection.ExecuteScalarAsync<long>(new CommandDefinition(
                $"SELECT count(*) FROM {row.Name}", cancellationToken: ct));
            row.RowCount = count;
        }
        return rows;
    }

    public async Task<IReadOnlyList<ColumnInfo>> GetColumnsAsync(string table, CancellationToken ct = default)
    {
        EnsureReadable(table);
        const string sql = """
            SELECT column_name AS Name, data_type AS DataType,
                   (is_nullable = 'YES') AS Nullable
            FROM information_schema.columns
            WHERE table_schema = 'public' AND table_name = @table
            ORDER BY ordinal_position
            """;
        await using var connection = await _factory.OpenAsync(ct);
        var rows = await connection.QueryAsync<ColumnInfo>(
            new CommandDefinition(sql, new { table }, cancellationToken: ct));
        return rows.ToList();
    }
}
