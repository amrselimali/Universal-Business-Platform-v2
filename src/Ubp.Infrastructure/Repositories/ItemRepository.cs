using Dapper;
using Ubp.Core.Models;
using Ubp.Core.Paging;
using Ubp.Infrastructure.Data;

namespace Ubp.Infrastructure.Repositories;

public interface IItemRepository
{
    Task<PagedResult<Item>> ListAsync(PageRequest request, Guid? categoryId, string? itemType,
        CancellationToken ct = default);
    Task<IReadOnlyList<ItemOption>> ListOptionsAsync(string? search, int take = 25,
        CancellationToken ct = default);
    Task<Item?> GetAsync(Guid id, CancellationToken ct = default);
    Task<ItemDetail?> GetDetailAsync(Guid id, CancellationToken ct = default);
    Task<Item> CreateAsync(ItemInput input, CancellationToken ct = default);
    Task<Item?> UpdateAsync(Guid id, int version, ItemInput input, CancellationToken ct = default);
    Task<bool> DeleteAsync(Guid id, CancellationToken ct = default);
    Task<Item?> SetActiveAsync(Guid id, bool isActive, CancellationToken ct = default);
    Task<bool> HasStockAsync(Guid id, CancellationToken ct = default);
}

public sealed partial class ItemRepository : IItemRepository
{
    private const string Columns = """
        i.id AS Id, i.code AS Code, i.name_ar AS NameAr, i.name_en AS NameEn,
        i.category_id AS CategoryId,
        (SELECT c.name_ar FROM item_categories c WHERE c.id = i.category_id) AS CategoryName,
        i.base_unit_id AS BaseUnitId,
        (SELECT u.name_ar FROM units u WHERE u.id = i.base_unit_id) AS BaseUnitName,
        i.item_type AS ItemType, i.notes AS Notes, i.is_active AS IsActive,
        i.tax_id AS TaxId, (SELECT t.name_ar FROM taxes t WHERE t.id = i.tax_id) AS TaxName,
        i.price_includes_tax AS PriceIncludesTax, i.track_expiry AS TrackExpiry,
        i.is_consumable AS IsConsumable, i.default_price AS DefaultPrice,
        i.custom_fields::text AS CustomFieldsJson,
        i.created_at AS CreatedAt, i.updated_at AS UpdatedAt, i.version AS Version
        """;

    private static readonly IReadOnlyDictionary<string, string> SortMap =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["code"] = "i.code", ["name_ar"] = "i.name_ar", ["created_at"] = "i.created_at"
        };

    private readonly IDbConnectionFactory _factory;

    public ItemRepository(IDbConnectionFactory factory) => _factory = factory;

    public async Task<PagedResult<Item>> ListAsync(PageRequest request, Guid? categoryId,
        string? itemType, CancellationToken ct = default)
    {
        var where = new List<string>();
        if (!request.IncludeInactive) where.Add("i.is_active");
        if (categoryId.HasValue) where.Add("i.category_id = @categoryId");
        if (!string.IsNullOrWhiteSpace(itemType)) where.Add("i.item_type = @itemType");
        if (!string.IsNullOrWhiteSpace(request.Search))
            where.Add("i.search_text LIKE '%' || normalize_search(@search) || '%'");

        var whereSql = where.Count == 0 ? "WHERE true" : "WHERE " + string.Join(" AND ", where);
        var order = SqlSort.Build(request.SortBy, SortMap, "i.code", request.Descending);
        var sql = $"""
            SELECT {Columns} FROM items i {whereSql}
            ORDER BY {order} LIMIT @take OFFSET @offset;
            SELECT count(*) FROM items i {whereSql};
            """;

        await using var connection = await _factory.OpenAsync(ct);
        using var multi = await connection.QueryMultipleAsync(new CommandDefinition(sql, new
        {
            search = request.Search, categoryId, itemType,
            take = request.SafePageSize, offset = request.Offset
        }, cancellationToken: ct));
        var items = (await multi.ReadAsync<ItemRow>()).Select(Map).ToList();
        var total = await multi.ReadFirstAsync<int>();
        return new PagedResult<Item>
        {
            Items = items, Total = total, Page = request.SafePage, PageSize = request.SafePageSize
        };
    }

    public async Task<IReadOnlyList<ItemOption>> ListOptionsAsync(string? search, int take = 25,
        CancellationToken ct = default)
    {
        take = take is < 1 or > 50 ? 25 : take;
        const string sql = """
            SELECT i.id AS Id, i.code AS Code, i.name_ar AS NameAr, i.name_en AS NameEn,
                   i.base_unit_id AS BaseUnitId,
                   (SELECT u.name_ar FROM units u WHERE u.id = i.base_unit_id) AS BaseUnitName,
                   COALESCE((SELECT sum(b.on_hand) FROM stock_balances b WHERE b.item_id = i.id), 0) AS OnHand
            FROM items i
            WHERE i.is_active AND i.item_type = 'GOODS'
              AND (@search IS NULL OR @search = '' OR i.search_text LIKE '%' || normalize_search(@search) || '%')
            ORDER BY i.name_ar
            LIMIT @take
            """;
        await using var connection = await _factory.OpenAsync(ct);
        var rows = await connection.QueryAsync<ItemOption>(new CommandDefinition(sql,
            new { search, take }, cancellationToken: ct));
        return rows.ToList();
    }

    public async Task<Item?> GetAsync(Guid id, CancellationToken ct = default)
    {
        await using var connection = await _factory.OpenAsync(ct);
        var row = await connection.QuerySingleOrDefaultAsync<ItemRow>(new CommandDefinition(
            $"SELECT {Columns} FROM items i WHERE i.id = @id", new { id }, cancellationToken: ct));
        return row is null ? null : Map(row);
    }

    private static Item Map(ItemRow row) => new()
    {
        Id = row.Id, Code = row.Code, NameAr = row.NameAr, NameEn = row.NameEn,
        CategoryId = row.CategoryId, CategoryName = row.CategoryName,
        BaseUnitId = row.BaseUnitId, BaseUnitName = row.BaseUnitName,
        ItemType = row.ItemType, Notes = row.Notes, IsActive = row.IsActive,
        CustomFields = string.IsNullOrWhiteSpace(row.CustomFieldsJson)
            ? new() : System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object?>>(row.CustomFieldsJson) ?? new(),
        CreatedAt = row.CreatedAt, UpdatedAt = row.UpdatedAt, Version = row.Version
    };

    private sealed class ItemRow
    {
        public Guid Id { get; set; }
        public string Code { get; set; } = "";
        public string NameAr { get; set; } = "";
        public string? NameEn { get; set; }
        public Guid? CategoryId { get; set; }
        public string? CategoryName { get; set; }
        public Guid BaseUnitId { get; set; }
        public string BaseUnitName { get; set; } = "";
        public string ItemType { get; set; } = "GOODS";
        public string? Notes { get; set; }
        public bool IsActive { get; set; }
        public string? CustomFieldsJson { get; set; }
        public DateTimeOffset CreatedAt { get; set; }
        public DateTimeOffset UpdatedAt { get; set; }
        public int Version { get; set; }
    }

    public async Task<ItemDetail?> GetDetailAsync(Guid id, CancellationToken ct = default)
    {
        var item = await GetAsync(id, ct);
        if (item is null) return null;

        const string unitsSql = """
            SELECT iu.id AS Id, iu.item_id AS ItemId, iu.unit_id AS UnitId,
                   u.name_ar AS UnitName, u.code AS UnitCode,
                   iu.conversion_factor AS ConversionFactor, iu.sale_price AS SalePrice,
                   iu.purchase_price AS PurchasePrice, iu.is_active AS IsActive, iu.version AS Version
            FROM item_units iu JOIN units u ON u.id = iu.unit_id
            WHERE iu.item_id = @id ORDER BY iu.conversion_factor
            """;
        const string barcodesSql = """
            SELECT b.id AS Id, b.item_id AS ItemId, b.unit_id AS UnitId,
                   (SELECT u.name_ar FROM units u WHERE u.id = b.unit_id) AS UnitName,
                   b.barcode AS Barcode, b.is_active AS IsActive
            FROM item_barcodes b WHERE b.item_id = @id ORDER BY b.barcode
            """;

        await using var connection = await _factory.OpenAsync(ct);
        var units = (await connection.QueryAsync<ItemUnit>(new CommandDefinition(unitsSql, new { id }, cancellationToken: ct))).ToList();
        var barcodes = (await connection.QueryAsync<ItemBarcode>(new CommandDefinition(barcodesSql, new { id }, cancellationToken: ct))).ToList();
        return new ItemDetail { Item = item, Units = units, Barcodes = barcodes };
    }

    public async Task<bool> HasStockAsync(Guid id, CancellationToken ct = default)
    {
        const string sql = """
            SELECT CASE WHEN to_regclass('public.stock_movements') IS NULL THEN false
                        ELSE EXISTS (SELECT 1 FROM stock_movements WHERE item_id = @id) END
            """;
        await using var connection = await _factory.OpenAsync(ct);
        return await connection.ExecuteScalarAsync<bool>(new CommandDefinition(sql, new { id }, cancellationToken: ct));
    }
}
