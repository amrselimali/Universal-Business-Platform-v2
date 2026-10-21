using Dapper;
using Ubp.Core.Models;
using Ubp.Core.Paging;
using Ubp.Infrastructure.Data;

namespace Ubp.Infrastructure.Repositories;

public interface IPartnerRepository
{
    Task<PagedResult<Partner>> ListAsync(PartnerKind kind, PageRequest request, Guid? categoryId,
        bool isAdmin, IReadOnlyCollection<Guid> branchIds, CancellationToken ct = default);
    Task<Partner?> GetAsync(PartnerKind kind, Guid id, CancellationToken ct = default);
    Task<Partner> CreateAsync(PartnerKind kind, PartnerInput input, CancellationToken ct = default);
    Task<Partner?> UpdateAsync(PartnerKind kind, Guid id, int version, PartnerInput input,
        CancellationToken ct = default);
    Task<bool> DeleteAsync(PartnerKind kind, Guid id, CancellationToken ct = default);
    Task<Partner?> SetActiveAsync(PartnerKind kind, Guid id, bool isActive, CancellationToken ct = default);

    Task<IReadOnlyList<PartnerCategory>> ListCategoriesAsync(PartnerKind kind, bool includeInactive,
        CancellationToken ct = default);
    Task<PartnerCategory?> GetCategoryAsync(PartnerKind kind, Guid id, CancellationToken ct = default);
    Task<PartnerCategory> CreateCategoryAsync(PartnerKind kind, PartnerCategoryInput input,
        CancellationToken ct = default);
    Task<PartnerCategory?> UpdateCategoryAsync(PartnerKind kind, Guid id, int version,
        PartnerCategoryInput input, CancellationToken ct = default);
    Task<bool> DeleteCategoryAsync(PartnerKind kind, Guid id, CancellationToken ct = default);
    Task<int> CountCategoryUsageAsync(PartnerKind kind, Guid id, CancellationToken ct = default);
}

public sealed partial class PartnerRepository : IPartnerRepository
{
    private readonly IDbConnectionFactory _factory;

    public PartnerRepository(IDbConnectionFactory factory) => _factory = factory;

    internal static string Table(PartnerKind kind) => kind == PartnerKind.Customer ? "customers" : "suppliers";
    internal static string CategoryTable(PartnerKind kind) =>
        kind == PartnerKind.Customer ? "customer_categories" : "supplier_categories";

    private static readonly IReadOnlyDictionary<string, string> SortMap =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["code"] = "p.code", ["name_ar"] = "p.name_ar", ["created_at"] = "p.created_at"
        };

    private static string Columns(PartnerKind kind)
    {
        var category = CategoryTable(kind);
        return $"""
            p.id AS Id, p.code AS Code, p.name_ar AS NameAr, p.name_en AS NameEn,
            p.category_id AS CategoryId,
            (SELECT c.name_ar FROM {category} c WHERE c.id = p.category_id) AS CategoryName,
            p.scope_type AS ScopeType, p.branch_id AS BranchId,
            (SELECT b.name_ar FROM branches b WHERE b.id = p.branch_id) AS BranchName,
            p.phone AS Phone, p.email AS Email, p.address AS Address, p.tax_number AS TaxNumber,
            p.credit_limit AS CreditLimit, p.notes AS Notes, p.is_active AS IsActive,
            p.custom_fields::text AS CustomFieldsJson,
            p.created_at AS CreatedAt, p.updated_at AS UpdatedAt, p.version AS Version
            """;
    }

    public async Task<PagedResult<Partner>> ListAsync(PartnerKind kind, PageRequest request,
        Guid? categoryId, bool isAdmin, IReadOnlyCollection<Guid> branchIds, CancellationToken ct = default)
    {
        var where = new List<string>();
        if (!request.IncludeInactive) where.Add("p.is_active");
        if (categoryId.HasValue) where.Add("p.category_id = @categoryId");
        if (!isAdmin) where.Add("(p.scope_type = 'COMPANY' OR p.branch_id = ANY(@branchIds))");
        if (!string.IsNullOrWhiteSpace(request.Search))
            where.Add("p.search_text LIKE '%' || normalize_search(@search) || '%'");

        var whereSql = where.Count == 0 ? "WHERE true" : "WHERE " + string.Join(" AND ", where);
        var order = SqlSort.Build(request.SortBy, SortMap, "p.code", request.Descending);
        var sql = $"""
            SELECT {Columns(kind)} FROM {Table(kind)} p {whereSql}
            ORDER BY {order} LIMIT @take OFFSET @offset;
            SELECT count(*) FROM {Table(kind)} p {whereSql};
            """;

        await using var connection = await _factory.OpenAsync(ct);
        using var multi = await connection.QueryMultipleAsync(new CommandDefinition(sql, new
        {
            search = request.Search, categoryId, branchIds = branchIds.ToArray(),
            take = request.SafePageSize, offset = request.Offset
        }, cancellationToken: ct));
        var items = (await multi.ReadAsync<PartnerRow>()).Select(Map).ToList();
        var total = await multi.ReadFirstAsync<int>();
        return new PagedResult<Partner>
        {
            Items = items, Total = total, Page = request.SafePage, PageSize = request.SafePageSize
        };
    }

    public async Task<Partner?> GetAsync(PartnerKind kind, Guid id, CancellationToken ct = default)
    {
        await using var connection = await _factory.OpenAsync(ct);
        var row = await connection.QuerySingleOrDefaultAsync<PartnerRow>(new CommandDefinition(
            $"SELECT {Columns(kind)} FROM {Table(kind)} p WHERE p.id = @id", new { id }, cancellationToken: ct));
        return row is null ? null : Map(row);
    }

    private static Partner Map(PartnerRow row) => new()
    {
        Id = row.Id, Code = row.Code, NameAr = row.NameAr, NameEn = row.NameEn,
        CategoryId = row.CategoryId, CategoryName = row.CategoryName,
        ScopeType = row.ScopeType, BranchId = row.BranchId, BranchName = row.BranchName,
        Phone = row.Phone, Email = row.Email, Address = row.Address, TaxNumber = row.TaxNumber,
        CreditLimit = row.CreditLimit, Notes = row.Notes, IsActive = row.IsActive,
        CustomFields = string.IsNullOrWhiteSpace(row.CustomFieldsJson)
            ? new() : System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object?>>(row.CustomFieldsJson) ?? new(),
        CreatedAt = row.CreatedAt, UpdatedAt = row.UpdatedAt, Version = row.Version
    };

    private sealed class PartnerRow
    {
        public Guid Id { get; set; }
        public string Code { get; set; } = "";
        public string NameAr { get; set; } = "";
        public string? NameEn { get; set; }
        public Guid? CategoryId { get; set; }
        public string? CategoryName { get; set; }
        public string ScopeType { get; set; } = "COMPANY";
        public Guid? BranchId { get; set; }
        public string? BranchName { get; set; }
        public string? Phone { get; set; }
        public string? Email { get; set; }
        public string? Address { get; set; }
        public string? TaxNumber { get; set; }
        public decimal CreditLimit { get; set; }
        public string? Notes { get; set; }
        public bool IsActive { get; set; }
        public string? CustomFieldsJson { get; set; }
        public DateTimeOffset CreatedAt { get; set; }
        public DateTimeOffset UpdatedAt { get; set; }
        public int Version { get; set; }
    }

    public async Task<IReadOnlyList<PartnerCategory>> ListCategoriesAsync(PartnerKind kind,
        bool includeInactive, CancellationToken ct = default)
    {
        var sql = $"""
            SELECT c.id AS Id, c.code AS Code, c.name_ar AS NameAr, c.name_en AS NameEn,
                   (SELECT count(*)::int FROM {Table(kind)} p WHERE p.category_id = c.id) AS PartnerCount,
                   c.is_active AS IsActive, c.created_at AS CreatedAt, c.updated_at AS UpdatedAt,
                   c.version AS Version
            FROM {CategoryTable(kind)} c
            WHERE (@includeInactive OR c.is_active)
            ORDER BY c.code
            """;
        await using var connection = await _factory.OpenAsync(ct);
        var rows = await connection.QueryAsync<PartnerCategory>(new CommandDefinition(sql,
            new { includeInactive }, cancellationToken: ct));
        return rows.ToList();
    }

    public async Task<PartnerCategory?> GetCategoryAsync(PartnerKind kind, Guid id, CancellationToken ct = default)
    {
        await using var connection = await _factory.OpenAsync(ct);
        return await connection.QuerySingleOrDefaultAsync<PartnerCategory>(new CommandDefinition(
            $"""
            SELECT id AS Id, code AS Code, name_ar AS NameAr, name_en AS NameEn,
                   is_active AS IsActive, created_at AS CreatedAt, updated_at AS UpdatedAt, version AS Version
            FROM {CategoryTable(kind)} WHERE id = @id
            """, new { id }, cancellationToken: ct));
    }

    public async Task<int> CountCategoryUsageAsync(PartnerKind kind, Guid id, CancellationToken ct = default)
    {
        await using var connection = await _factory.OpenAsync(ct);
        return await connection.ExecuteScalarAsync<int>(new CommandDefinition(
            $"SELECT count(*)::int FROM {Table(kind)} WHERE category_id = @id", new { id }, cancellationToken: ct));
    }
}
