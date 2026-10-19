using System.Text.Json;
using Dapper;
using Ubp.Core.Models;
using Ubp.Infrastructure.Data;

namespace Ubp.Infrastructure.Repositories;

public sealed partial class BranchRepository
{
    public async Task<Branch?> GetAsync(Guid id, CancellationToken ct = default)
    {
        var sql = $"SELECT {SelectColumns} FROM branches b WHERE b.id = @id";
        await using var connection = await _factory.OpenAsync(ct);
        var row = await connection.QuerySingleOrDefaultAsync<BranchRow>(
            new CommandDefinition(sql, new { id }, cancellationToken: ct));
        return row is null ? null : Map(row);
    }

    public async Task<Branch> CreateAsync(BranchInput input, CancellationToken ct = default)
    {
        const string sql = """
            INSERT INTO branches (code, name_ar, name_en, address, phone, is_active, custom_fields)
            VALUES (@Code, @NameAr, @NameEn, @Address, @Phone, @IsActive, CAST(@CustomFields AS jsonb))
            RETURNING id
            """;
        await using var connection = await _factory.OpenAsync(ct);
        var id = await connection.ExecuteScalarAsync<Guid>(new CommandDefinition(sql, new
        {
            input.Code, input.NameAr, input.NameEn, input.Address, input.Phone, input.IsActive,
            CustomFields = JsonSerializer.Serialize(input.CustomFields)
        }, cancellationToken: ct));
        return (await GetAsync(id, ct))!;
    }

    public async Task<Branch?> UpdateAsync(Guid id, int version, BranchInput input, CancellationToken ct = default)
    {
        const string sql = """
            UPDATE branches SET
                code = @Code, name_ar = @NameAr, name_en = @NameEn, address = @Address,
                phone = @Phone, is_active = @IsActive, custom_fields = CAST(@CustomFields AS jsonb)
            WHERE id = @id AND version = @version
            RETURNING id
            """;
        await using var connection = await _factory.OpenAsync(ct);
        var updated = await connection.ExecuteScalarAsync<Guid?>(new CommandDefinition(sql, new
        {
            id, version, input.Code, input.NameAr, input.NameEn, input.Address, input.Phone,
            input.IsActive, CustomFields = JsonSerializer.Serialize(input.CustomFields)
        }, cancellationToken: ct));
        return updated is null ? null : await GetAsync(id, ct);
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        await using var connection = await _factory.OpenAsync(ct);
        var rows = await connection.ExecuteAsync(
            new CommandDefinition("DELETE FROM branches WHERE id = @id", new { id }, cancellationToken: ct));
        return rows > 0;
    }

    public async Task<Branch?> SetActiveAsync(Guid id, bool isActive, CancellationToken ct = default)
    {
        await using var connection = await _factory.OpenAsync(ct);
        var rows = await connection.ExecuteAsync(new CommandDefinition(
            "UPDATE branches SET is_active = @isActive WHERE id = @id", new { id, isActive }, cancellationToken: ct));
        return rows == 0 ? null : await GetAsync(id, ct);
    }

    public async Task<(int Users, int Warehouses)> CountLinksAsync(Guid id, CancellationToken ct = default)
    {
        const string sql = """
            SELECT (SELECT count(*)::int FROM user_branches WHERE branch_id = @id),
                   (SELECT count(*)::int FROM warehouse_branches WHERE branch_id = @id)
            """;
        await using var connection = await _factory.OpenAsync(ct);
        return await connection.QuerySingleAsync<(int, int)>(
            new CommandDefinition(sql, new { id }, cancellationToken: ct));
    }

    private static Branch Map(BranchRow row) => new()
    {
        Id = row.Id, Code = row.Code, NameAr = row.NameAr, NameEn = row.NameEn,
        Address = row.Address, Phone = row.Phone, IsActive = row.IsActive,
        CustomFields = string.IsNullOrWhiteSpace(row.CustomFieldsJson)
            ? new() : JsonSerializer.Deserialize<Dictionary<string, object?>>(row.CustomFieldsJson) ?? new(),
        CreatedAt = row.CreatedAt, UpdatedAt = row.UpdatedAt, Version = row.Version
    };

    private sealed class BranchRow
    {
        public Guid Id { get; set; }
        public string Code { get; set; } = "";
        public string NameAr { get; set; } = "";
        public string? NameEn { get; set; }
        public string? Address { get; set; }
        public string? Phone { get; set; }
        public bool IsActive { get; set; }
        public string? CustomFieldsJson { get; set; }
        public DateTimeOffset CreatedAt { get; set; }
        public DateTimeOffset UpdatedAt { get; set; }
        public int Version { get; set; }
    }
}
