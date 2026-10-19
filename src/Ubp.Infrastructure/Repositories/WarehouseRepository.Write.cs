using System.Text.Json;
using Dapper;
using Ubp.Core.Models;
using Ubp.Infrastructure.Data;

namespace Ubp.Infrastructure.Repositories;

public sealed partial class WarehouseRepository
{
    public async Task<Warehouse?> GetAsync(Guid id, CancellationToken ct = default)
    {
        var sql = $"""
            SELECT {SelectColumns} FROM warehouses w {Joins}
            WHERE w.id = @id GROUP BY w.id
            """;
        await using var connection = await _factory.OpenAsync(ct);
        var row = await connection.QuerySingleOrDefaultAsync<WarehouseRow>(
            new CommandDefinition(sql, new { id }, cancellationToken: ct));
        return row is null ? null : Map(row);
    }

    public async Task<Warehouse> CreateAsync(WarehouseInput input, CancellationToken ct = default)
    {
        const string insert = """
            INSERT INTO warehouses (code, name_ar, name_en, is_active, custom_fields)
            VALUES (@Code, @NameAr, @NameEn, @IsActive, CAST(@CustomFields AS jsonb))
            RETURNING id
            """;
        const string link = """
            INSERT INTO warehouse_branches (warehouse_id, branch_id)
            VALUES (@warehouseId, @branchId) ON CONFLICT DO NOTHING
            """;

        await using var connection = await _factory.OpenAsync(ct);
        await using var tx = await connection.BeginTransactionAsync(ct);
        var id = await connection.ExecuteScalarAsync<Guid>(insert, new
        {
            input.Code, input.NameAr, input.NameEn, input.IsActive,
            CustomFields = JsonSerializer.Serialize(input.CustomFields)
        }, tx);
        foreach (var branchId in input.BranchIds.Distinct())
            await connection.ExecuteAsync(link, new { warehouseId = id, branchId }, tx);
        await tx.CommitAsync(ct);
        return (await GetAsync(id, ct))!;
    }

    public async Task<Warehouse?> UpdateAsync(Guid id, int version, WarehouseInput input, CancellationToken ct = default)
    {
        const string update = """
            UPDATE warehouses SET
                code = @Code, name_ar = @NameAr, name_en = @NameEn,
                is_active = @IsActive, custom_fields = CAST(@CustomFields AS jsonb)
            WHERE id = @id AND version = @version
            RETURNING id
            """;

        await using var connection = await _factory.OpenAsync(ct);
        await using var tx = await connection.BeginTransactionAsync(ct);
        var updated = await connection.ExecuteScalarAsync<Guid?>(update, new
        {
            id, version, input.Code, input.NameAr, input.NameEn, input.IsActive,
            CustomFields = JsonSerializer.Serialize(input.CustomFields)
        }, tx);
        if (updated is null)
        {
            await tx.RollbackAsync(ct);
            return null;
        }

        var desired = input.BranchIds.Distinct().ToArray();
        await connection.ExecuteAsync(
            "DELETE FROM warehouse_branches WHERE warehouse_id = @id AND NOT (branch_id = ANY(@desired))",
            new { id, desired }, tx);
        foreach (var branchId in desired)
            await connection.ExecuteAsync("""
                INSERT INTO warehouse_branches (warehouse_id, branch_id)
                VALUES (@id, @branchId) ON CONFLICT DO NOTHING
                """, new { id, branchId }, tx);

        await tx.CommitAsync(ct);
        return await GetAsync(id, ct);
    }

    public async Task<Warehouse?> SetActiveAsync(Guid id, bool isActive, CancellationToken ct = default)
    {
        await using var connection = await _factory.OpenAsync(ct);
        var rows = await connection.ExecuteAsync(new CommandDefinition(
            "UPDATE warehouses SET is_active = @isActive WHERE id = @id",
            new { id, isActive }, cancellationToken: ct));
        return rows == 0 ? null : await GetAsync(id, ct);
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        await using var connection = await _factory.OpenAsync(ct);
        var rows = await connection.ExecuteAsync(
            new CommandDefinition("DELETE FROM warehouses WHERE id = @id", new { id }, cancellationToken: ct));
        return rows > 0;
    }

    public async Task<(int Users, int Branches)> CountLinksAsync(Guid id, CancellationToken ct = default)
    {
        const string sql = """
            SELECT (SELECT count(*)::int FROM user_warehouses WHERE warehouse_id = @id),
                   (SELECT count(*)::int FROM warehouse_branches WHERE warehouse_id = @id)
            """;
        await using var connection = await _factory.OpenAsync(ct);
        return await connection.QuerySingleAsync<(int, int)>(
            new CommandDefinition(sql, new { id }, cancellationToken: ct));
    }

    private static Warehouse Map(WarehouseRow row) => new()
    {
        Id = row.Id, Code = row.Code, NameAr = row.NameAr, NameEn = row.NameEn,
        IsActive = row.IsActive,
        CustomFields = string.IsNullOrWhiteSpace(row.CustomFieldsJson)
            ? new() : JsonSerializer.Deserialize<Dictionary<string, object?>>(row.CustomFieldsJson) ?? new(),
        BranchIds = row.BranchIds?.ToList() ?? new(),
        CreatedAt = row.CreatedAt, UpdatedAt = row.UpdatedAt, Version = row.Version
    };

    private sealed class WarehouseRow
    {
        public Guid Id { get; set; }
        public string Code { get; set; } = "";
        public string NameAr { get; set; } = "";
        public string? NameEn { get; set; }
        public bool IsActive { get; set; }
        public string? CustomFieldsJson { get; set; }
        public Guid[]? BranchIds { get; set; }
        public DateTimeOffset CreatedAt { get; set; }
        public DateTimeOffset UpdatedAt { get; set; }
        public int Version { get; set; }
    }
}
