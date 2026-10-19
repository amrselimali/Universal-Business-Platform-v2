using System.Text.Json;
using Dapper;
using Ubp.Infrastructure.Data;

namespace Ubp.Infrastructure.Repositories;

public sealed partial class SchemaAdminRepository
{
    public async Task<bool> UpdateRowAsync(string table, string id, Dictionary<string, object?> values,
        CancellationToken ct = default)
    {
        EnsureEditable(table);
        var columns = await GetColumnsAsync(table, ct);
        var metadata = columns
            .Where(c => !ProtectedColumns.Contains(c.Name) && values.ContainsKey(c.Name))
            .ToList();
        if (metadata.Count == 0) return false;

        var parameters = new DynamicParameters();
        parameters.Add("rowId", id);
        var sets = new List<string>();
        for (var i = 0; i < metadata.Count; i++)
        {
            var column = metadata[i];
            parameters.Add($"p{i}", ToDbValue(values[column.Name]));
            sets.Add($"\"{column.Name}\" = CAST(@p{i} AS {SqlType(column.DataType)})");
        }

        var hasVersion = columns.Any(c => c.Name == "version");
        var sql = $"UPDATE {table} SET {string.Join(", ", sets)} WHERE id::text = @rowId";
        if (hasVersion)
        {
            parameters.Add("expectedVersion", values.TryGetValue("version", out var v) ? ToInt(v) : -1);
            sql += " AND version = @expectedVersion";
        }
        sql += " RETURNING 1";

        await using var connection = await _factory.OpenAsync(ct);
        return await connection.ExecuteScalarAsync<int?>(new CommandDefinition(sql, parameters,
            cancellationToken: ct)) is not null;
    }

    public async Task<bool> DeleteRowAsync(string table, string id, CancellationToken ct = default)
    {
        EnsureEditable(table);
        await using var connection = await _factory.OpenAsync(ct);
        return await connection.ExecuteAsync(new CommandDefinition(
            $"DELETE FROM {table} WHERE id::text = @id", new { id }, cancellationToken: ct)) > 0;
    }

    private static object? ToDbValue(object? value)
    {
        if (value is null) return null;
        if (value is JsonElement element)
        {
            return element.ValueKind switch
            {
                JsonValueKind.Null => null,
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                JsonValueKind.Number => element.GetRawText(),
                JsonValueKind.String => element.GetString(),
                _ => element.GetRawText()
            };
        }
        return value;
    }

    private static int ToInt(object? value)
    {
        if (value is JsonElement e && e.TryGetInt32(out var number)) return number;
        return int.TryParse(value?.ToString(), out var parsed) ? parsed : -1;
    }

    private static string SqlType(string dataType) => dataType switch
    {
        "uuid" => "uuid",
        "jsonb" => "jsonb",
        "json" => "json",
        "inet" => "inet",
        "boolean" => "boolean",
        "smallint" => "smallint",
        "integer" => "integer",
        "bigint" => "bigint",
        "numeric" => "numeric",
        "real" => "real",
        "double precision" => "double precision",
        "date" => "date",
        "timestamp with time zone" => "timestamptz",
        "timestamp without time zone" => "timestamp",
        "text" => "text",
        "character varying" => "varchar",
        "character" => "char",
        _ => "text"
    };
}
