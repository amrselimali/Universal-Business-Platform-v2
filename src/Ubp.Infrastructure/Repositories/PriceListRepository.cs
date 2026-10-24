using Dapper;
using Ubp.Core.Models;
using Ubp.Core.Paging;
using Ubp.Infrastructure.Data;

namespace Ubp.Infrastructure.Repositories;

public interface IPriceListRepository
{
    Task<PagedResult<PriceList>> ListAsync(PageRequest request, bool includeInactive, CancellationToken ct = default);
    Task<IReadOnlyList<PriceList>> ListOptionsAsync(CancellationToken ct = default);
    Task<PriceList?> GetAsync(Guid id, CancellationToken ct = default);
    Task<PriceList?> GetDefaultAsync(CancellationToken ct = default);
    Task<Guid> CreateAsync(Guid id, PriceListInput input, Guid userId, CancellationToken ct = default);
    Task<PriceList?> UpdateAsync(Guid id, int version, PriceListInput input, Guid userId, CancellationToken ct = default);
    Task<PriceList?> SetActiveAsync(Guid id, bool isActive, Guid userId, CancellationToken ct = default);
    Task<bool> DeleteAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<PriceListItem>> ListItemsAsync(Guid priceListId, CancellationToken ct = default);
    Task ReplaceItemsAsync(Guid priceListId, IReadOnlyList<PriceListItemInput> items, CancellationToken ct = default);
    Task<decimal?> ResolvePriceAsync(Guid priceListId, Guid itemId, decimal quantity, CancellationToken ct = default);
}

public sealed class PriceListRepository : RepositoryBase, IPriceListRepository
{
    private const string Columns = """
        p.id AS Id, p.code AS Code, p.name_ar AS NameAr, p.name_en AS NameEn, p.currency AS Currency,
        p.is_default AS IsDefault, p.valid_from AS ValidFrom, p.valid_to AS ValidTo,
        p.notes AS Notes, p.is_active AS IsActive, p.version AS Version,
        (SELECT count(*)::int FROM price_list_items x WHERE x.price_list_id = p.id) AS ItemsCount
        FROM price_lists p
        """;

    public PriceListRepository(IDbConnectionFactory factory) : base(factory) { }

    public async Task<PagedResult<PriceList>> ListAsync(PageRequest request, bool includeInactive,
        CancellationToken ct = default)
    {
        var where = includeInactive ? "WHERE true" : "WHERE p.is_active";
        if (!string.IsNullOrWhiteSpace(request.Search))
            where += " AND (p.code ILIKE '%' || @search || '%' OR normalize_search(p.name_ar) " +
                     "LIKE '%' || normalize_search(@search) || '%')";
        return await PageAsync<PriceList>(
            $"SELECT {Columns} {where} ORDER BY p.is_default DESC, p.code LIMIT @take OFFSET @offset",
            $"SELECT count(*)::int FROM price_lists p {where}",
            new { search = request.Search, take = request.SafePageSize, offset = request.Offset },
            request, ct);
    }

    public Task<IReadOnlyList<PriceList>> ListOptionsAsync(CancellationToken ct = default) =>
        QueryAsync<PriceList>($"SELECT {Columns} WHERE p.is_active ORDER BY p.is_default DESC, p.code", null, ct);

    public Task<PriceList?> GetAsync(Guid id, CancellationToken ct = default) =>
        SingleAsync<PriceList>($"SELECT {Columns} WHERE p.id = @id", new { id }, ct);

    public Task<PriceList?> GetDefaultAsync(CancellationToken ct = default) =>
        SingleAsync<PriceList>($"SELECT {Columns} WHERE p.is_default AND p.is_active LIMIT 1", null, ct);

    public async Task<Guid> CreateAsync(Guid id, PriceListInput i, Guid userId,
        CancellationToken ct = default)
    {
        if (i.IsDefault)
            await ExecuteRowAsync("UPDATE price_lists SET is_default = false WHERE is_default", null, ct);
        return await InsertRowAsync("""
            INSERT INTO price_lists (id, code, name_ar, name_en, is_default, valid_from, valid_to,
                notes, is_active, created_by)
            VALUES (@id, @Code, @NameAr, @NameEn, @IsDefault, @ValidFrom, @ValidTo,
                    @Notes, @IsActive, @userId)
            RETURNING id
            """, new { id, i.Code, i.NameAr, i.NameEn, i.IsDefault, i.ValidFrom, i.ValidTo,
                i.Notes, i.IsActive, userId }, ct);
    }

    public async Task<PriceList?> UpdateAsync(Guid id, int version, PriceListInput i, Guid userId,
        CancellationToken ct = default)
    {
        if (i.IsDefault)
            await ExecuteRowAsync(
                "UPDATE price_lists SET is_default = false WHERE is_default AND id <> @id",
                new { id }, ct);
        var updated = await UpdateRowAsync("""
            UPDATE price_lists SET code = @Code, name_ar = @NameAr, name_en = @NameEn,
                is_default = @IsDefault, valid_from = @ValidFrom, valid_to = @ValidTo,
                notes = @Notes, is_active = @IsActive, updated_by = @userId
            WHERE id = @id AND version = @version RETURNING id
            """, new { id, version, i.Code, i.NameAr, i.NameEn, i.IsDefault, i.ValidFrom,
                i.ValidTo, i.Notes, i.IsActive, userId }, ct);
        return updated is null ? null : await GetAsync(id, ct);
    }

    public async Task<PriceList?> SetActiveAsync(Guid id, bool isActive, Guid userId,
        CancellationToken ct = default)
    {
        var rows = await ExecuteRowAsync(
            "UPDATE price_lists SET is_active = @isActive, updated_by = @userId WHERE id = @id",
            new { id, isActive, userId }, ct);
        return rows == 0 ? null : await GetAsync(id, ct);
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken ct = default) =>
        await ExecuteRowAsync("DELETE FROM price_lists WHERE id = @id", new { id }, ct) > 0;

    public Task<IReadOnlyList<PriceListItem>> ListItemsAsync(Guid priceListId,
        CancellationToken ct = default) =>
        QueryAsync<PriceListItem>("""
            SELECT x.id AS Id, x.price_list_id AS PriceListId, x.item_id AS ItemId,
                   i.code AS ItemCode, i.name_ar AS ItemName, x.unit_id AS UnitId,
                   u.name_ar AS UnitName, x.price AS Price, x.min_qty AS MinQty
            FROM price_list_items x
            JOIN items i ON i.id = x.item_id
            LEFT JOIN units u ON u.id = x.unit_id
            WHERE x.price_list_id = @priceListId
            ORDER BY i.code, x.min_qty
            """, new { priceListId }, ct);

    /// <summary>استبدال بنود القائمة داخل معاملة واحدة (حفظ الشاشة = Transaction).</summary>
    public async Task ReplaceItemsAsync(Guid priceListId, IReadOnlyList<PriceListItemInput> items,
        CancellationToken ct = default)
    {
        await using var connection = await Factory.OpenAsync(ct);
        await using var tx = await connection.BeginTransactionAsync(ct);
        await connection.ExecuteAsync(new CommandDefinition(
            "DELETE FROM price_list_items WHERE price_list_id = @priceListId",
            new { priceListId }, tx, cancellationToken: ct));

        foreach (var item in items)
        {
            await connection.ExecuteAsync(new CommandDefinition("""
                INSERT INTO price_list_items (id, price_list_id, item_id, unit_id, price, min_qty)
                VALUES (@id, @priceListId, @ItemId, @UnitId, @Price, @MinQty)
                ON CONFLICT (price_list_id, item_id, unit_id, min_qty)
                DO UPDATE SET price = EXCLUDED.price
                """, new
            {
                id = Guid.CreateVersion7(), priceListId, item.ItemId, item.UnitId,
                item.Price, item.MinQty
            }, tx, cancellationToken: ct));
        }

        await tx.CommitAsync(ct);
    }

    /// <summary>سعر البيع: أعلى شريحة كمية لا تتجاوز الكمية المطلوبة.</summary>
    public Task<decimal?> ResolvePriceAsync(Guid priceListId, Guid itemId, decimal quantity,
        CancellationToken ct = default) =>
        SingleAsync<decimal?>("""
            SELECT price FROM price_list_items
            WHERE price_list_id = @priceListId AND item_id = @itemId AND min_qty <= @quantity
            ORDER BY min_qty DESC LIMIT 1
            """, new { priceListId, itemId, quantity }, ct);
}

