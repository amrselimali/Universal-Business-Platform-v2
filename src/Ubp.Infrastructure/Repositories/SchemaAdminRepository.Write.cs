using System.Text.Json;
using Dapper;
using Ubp.Core.Models;
using Ubp.Infrastructure.Data;

namespace Ubp.Infrastructure.Repositories;

public sealed partial class SchemaAdminRepository
{
    private static readonly HashSet<string> EditableTables = new(StringComparer.Ordinal)
    {
        "app_settings", "branches", "warehouses", "warehouse_branches", "currencies",
        "custom_field_definitions", "roles", "role_permissions", "user_roles",
        "user_permissions", "user_branches", "user_warehouses", "users"
    };

    private static readonly HashSet<string> ReadableTables = new(StringComparer.Ordinal)
    {
        "app_settings", "branches", "warehouses", "warehouse_branches", "currencies",
        "custom_field_definitions", "roles", "role_permissions", "user_roles",
        "user_permissions", "user_branches", "user_warehouses", "users",
        "refresh_tokens", "user_sessions", "audit_log",
        "qa_test_definitions", "qa_test_runs", "qa_test_results", "qa_test_artifacts",
        "document_sequences", "schemaversions",
        "units", "item_categories", "items", "item_units", "item_barcodes",
        "customer_categories", "customers", "supplier_categories", "suppliers",
        "fiscal_periods", "period_scope_closures", "period_close_checks", "period_events",
        "stock_documents", "stock_document_lines", "stock_movements", "stock_balances",
        "stock_transfer_receipts", "stock_transfer_receipt_lines", "stocktakes", "stocktake_lines"
    };

    public async Task<RowPage> GetRowsAsync(string table, int page, CancellationToken ct = default)
    {
        EnsureReadable(table);
        page = page < 1 ? 1 : page;
        var columns = await GetColumnsAsync(table, ct);
        var columnNames = columns.Select(c => c.Name).ToList();
        var idColumn = columnNames.Contains("id") ? "id" : columnNames.First();
        var order = table == "audit_log" ? "id DESC" : $"{idColumn} ASC";
        var sql = $"""
            SELECT row_to_json(t)::text FROM (
                SELECT * FROM {table} ORDER BY {order} LIMIT 25 OFFSET @offset
            ) t;
            SELECT count(*) FROM {table};
            """;
        await using var connection = await _factory.OpenAsync(ct);
        using var multi = await connection.QueryMultipleAsync(
            new CommandDefinition(sql, new { offset = (page - 1) * 25 }, cancellationToken: ct));
        var rows = (await multi.ReadAsync<string>()).ToList();
        var total = await multi.ReadFirstAsync<int>();
        return new RowPage(columnNames, rows, total);
    }

    public async Task<Dictionary<string, object?>?> GetRowAsync(string table, string id,
        CancellationToken ct = default)
    {
        EnsureReadable(table);
        await using var connection = await _factory.OpenAsync(ct);
        var json = await connection.ExecuteScalarAsync<string?>(new CommandDefinition(
            $"SELECT row_to_json(t)::text FROM (SELECT * FROM {table} WHERE id::text = @id) t",
            new { id }, cancellationToken: ct));
        return json is null ? null : JsonSerializer.Deserialize<Dictionary<string, object?>>(json);
    }

    private static void EnsureReadable(string table)
    {
        if (!ReadableTables.Contains(table))
            throw new InvalidOperationException("TABLE_NOT_ALLOWED");
    }

    internal static void EnsureEditable(string table)
    {
        if (!EditableTables.Contains(table))
            throw new InvalidOperationException("TABLE_READ_ONLY");
    }
}
