using System.Text.Json;
using Dapper;
using Ubp.Core.Models;
using Ubp.Infrastructure.Data;

namespace Ubp.Infrastructure.Repositories;

public sealed partial class CustomFieldRepository
{
    public async Task<CustomFieldDefinition?> GetAsync(Guid id, CancellationToken ct = default)
    {
        await using var connection = await _factory.OpenAsync(ct);
        var row = await connection.QuerySingleOrDefaultAsync<Row>(new CommandDefinition(
            $"SELECT {Columns} FROM custom_field_definitions WHERE id = @id", new { id }, cancellationToken: ct));
        return row is null ? null : Map(row);
    }

    public async Task<CustomFieldDefinition> CreateAsync(CustomFieldInput input, CancellationToken ct = default)
    {
        const string sql = """
            INSERT INTO custom_field_definitions
                (entity_name, field_key, label_ar, label_en, data_type, options, is_required, is_active, sort_order)
            VALUES (@EntityName, @FieldKey, @LabelAr, @LabelEn, @DataType, CAST(@Options AS jsonb),
                    @IsRequired, @IsActive, @SortOrder)
            RETURNING id
            """;
        await using var connection = await _factory.OpenAsync(ct);
        var id = await connection.ExecuteScalarAsync<Guid>(sql, ToParams(input));
        return (await GetAsync(id, ct))!;
    }

    public async Task<CustomFieldDefinition?> UpdateAsync(Guid id, int version, CustomFieldInput input,
        CancellationToken ct = default)
    {
        const string sql = """
            UPDATE custom_field_definitions SET
                entity_name = @EntityName, field_key = @FieldKey, label_ar = @LabelAr,
                label_en = @LabelEn, data_type = @DataType, options = CAST(@Options AS jsonb),
                is_required = @IsRequired, is_active = @IsActive, sort_order = @SortOrder
            WHERE id = @id AND version = @version
            RETURNING id
            """;
        await using var connection = await _factory.OpenAsync(ct);
        var p = ToParams(input);
        p.Add("id", id);
        p.Add("version", version);
        var updated = await connection.ExecuteScalarAsync<Guid?>(sql, p);
        return updated is null ? null : await GetAsync(id, ct);
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        await using var connection = await _factory.OpenAsync(ct);
        return await connection.ExecuteAsync(
            new CommandDefinition("DELETE FROM custom_field_definitions WHERE id = @id",
                new { id }, cancellationToken: ct)) > 0;
    }

    private static DynamicParameters ToParams(CustomFieldInput input)
    {
        var p = new DynamicParameters();
        p.Add("EntityName", input.EntityName);
        p.Add("FieldKey", input.FieldKey);
        p.Add("LabelAr", input.LabelAr);
        p.Add("LabelEn", input.LabelEn);
        p.Add("DataType", input.DataType);
        p.Add("Options", JsonSerializer.Serialize(input.Options));
        p.Add("IsRequired", input.IsRequired);
        p.Add("IsActive", input.IsActive);
        p.Add("SortOrder", input.SortOrder);
        return p;
    }

    private static CustomFieldDefinition Map(Row row) => new()
    {
        Id = row.Id, EntityName = row.EntityName, FieldKey = row.FieldKey, LabelAr = row.LabelAr,
        LabelEn = row.LabelEn, DataType = row.DataType,
        Options = string.IsNullOrWhiteSpace(row.OptionsJson)
            ? new() : JsonSerializer.Deserialize<List<string>>(row.OptionsJson) ?? new(),
        IsRequired = row.IsRequired, IsActive = row.IsActive, SortOrder = row.SortOrder,
        CreatedAt = row.CreatedAt, UpdatedAt = row.UpdatedAt, Version = row.Version
    };

    private sealed class Row
    {
        public Guid Id { get; set; }
        public string EntityName { get; set; } = "";
        public string FieldKey { get; set; } = "";
        public string LabelAr { get; set; } = "";
        public string? LabelEn { get; set; }
        public string DataType { get; set; } = "text";
        public string? OptionsJson { get; set; }
        public bool IsRequired { get; set; }
        public bool IsActive { get; set; }
        public int SortOrder { get; set; }
        public DateTimeOffset CreatedAt { get; set; }
        public DateTimeOffset UpdatedAt { get; set; }
        public int Version { get; set; }
    }
}
