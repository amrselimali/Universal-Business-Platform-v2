using Dapper;
using Ubp.Core.Models;
using Ubp.Core.Paging;
using Ubp.Infrastructure.Data;

namespace Ubp.Infrastructure.Repositories;

public interface ICustomFieldRepository
{
    Task<PagedResult<CustomFieldDefinition>> ListAsync(PageRequest request, string? entityName, CancellationToken ct = default);
    Task<IReadOnlyList<CustomFieldDefinition>> ListActiveAsync(string entityName, CancellationToken ct = default);
    Task<CustomFieldDefinition?> GetAsync(Guid id, CancellationToken ct = default);
    Task<CustomFieldDefinition> CreateAsync(CustomFieldInput input, CancellationToken ct = default);
    Task<CustomFieldDefinition?> UpdateAsync(Guid id, int version, CustomFieldInput input, CancellationToken ct = default);
    Task<bool> DeleteAsync(Guid id, CancellationToken ct = default);
}

public sealed partial class CustomFieldRepository : ICustomFieldRepository
{
    private readonly IDbConnectionFactory _factory;

    private const string Columns = """
        id AS Id, entity_name AS EntityName, field_key AS FieldKey, label_ar AS LabelAr,
        label_en AS LabelEn, data_type AS DataType, options::text AS OptionsJson,
        is_required AS IsRequired, is_active AS IsActive, sort_order AS SortOrder,
        created_at AS CreatedAt, updated_at AS UpdatedAt, version AS Version
        """;

    public CustomFieldRepository(IDbConnectionFactory factory) => _factory = factory;

    public async Task<PagedResult<CustomFieldDefinition>> ListAsync(PageRequest request, string? entityName,
        CancellationToken ct = default)
    {
        var where = new List<string> { "(@entity IS NULL OR entity_name = @entity)" };
        if (!string.IsNullOrWhiteSpace(request.Search))
            where.Add("normalize_search(field_key || ' ' || label_ar || ' ' || coalesce(label_en,'')) LIKE '%' || normalize_search(@search) || '%'");
        var whereSql = "WHERE " + string.Join(" AND ", where);
        var sql = $"""
            SELECT {Columns} FROM custom_field_definitions {whereSql}
            ORDER BY entity_name, sort_order, field_key LIMIT @take OFFSET @offset;
            SELECT count(*) FROM custom_field_definitions {whereSql};
            """;
        await using var connection = await _factory.OpenAsync(ct);
        using var multi = await connection.QueryMultipleAsync(new CommandDefinition(sql, new
        {
            entity = string.IsNullOrWhiteSpace(entityName) ? null : entityName,
            search = request.Search,
            take = request.SafePageSize,
            offset = request.Offset
        }, cancellationToken: ct));
        var rows = (await multi.ReadAsync<Row>()).ToList();
        var total = await multi.ReadFirstAsync<int>();
        return new PagedResult<CustomFieldDefinition>
        {
            Items = rows.Select(Map).ToList(), Total = total,
            Page = request.SafePage, PageSize = request.SafePageSize
        };
    }

    public async Task<IReadOnlyList<CustomFieldDefinition>> ListActiveAsync(string entityName,
        CancellationToken ct = default)
    {
        await using var connection = await _factory.OpenAsync(ct);
        var rows = await connection.QueryAsync<Row>(new CommandDefinition(
            $"SELECT {Columns} FROM custom_field_definitions WHERE entity_name = @entity AND is_active ORDER BY sort_order, field_key",
            new { entity = entityName }, cancellationToken: ct));
        return rows.Select(Map).ToList();
    }
}
