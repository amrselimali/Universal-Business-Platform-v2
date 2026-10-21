using System.Text.Json;
using Dapper;
using Npgsql;
using Ubp.Core.Models;

namespace Ubp.Infrastructure.Repositories;

public sealed partial class ItemRepository
{
    public async Task<Item> CreateAsync(ItemInput input, CancellationToken ct = default)
    {
        const string sql = """
            INSERT INTO items (code, name_ar, name_en, category_id, base_unit_id, item_type, notes,
                               is_active, custom_fields)
            VALUES (@Code, @NameAr, @NameEn, @CategoryId, @BaseUnitId, @ItemType, @Notes,
                    @IsActive, CAST(@CustomFields AS jsonb))
            RETURNING id
            """;
        await using var connection = await _factory.OpenAsync(ct);
        await using var tx = await connection.BeginTransactionAsync(ct);
        var id = await connection.ExecuteScalarAsync<Guid>(new CommandDefinition(sql, new
        {
            input.Code, input.NameAr, input.NameEn, input.CategoryId, input.BaseUnitId,
            input.ItemType, input.Notes, input.IsActive,
            CustomFields = JsonSerializer.Serialize(input.CustomFields)
        }, tx, cancellationToken: ct));

        await ReplaceUnitsAsync(connection, tx, id, input, ct);
        await ReplaceBarcodesAsync(connection, tx, id, input.Barcodes, ct);
        await tx.CommitAsync(ct);
        return (await GetAsync(id, ct))!;
    }

    public async Task<Item?> UpdateAsync(Guid id, int version, ItemInput input, CancellationToken ct = default)
    {
        const string sql = """
            UPDATE items SET code = @Code, name_ar = @NameAr, name_en = @NameEn,
                             category_id = @CategoryId, base_unit_id = @BaseUnitId,
                             item_type = @ItemType, notes = @Notes, is_active = @IsActive,
                             custom_fields = CAST(@CustomFields AS jsonb)
            WHERE id = @id AND version = @version
            RETURNING id
            """;
        await using var connection = await _factory.OpenAsync(ct);
        await using var tx = await connection.BeginTransactionAsync(ct);
        var updated = await connection.ExecuteScalarAsync<Guid?>(new CommandDefinition(sql, new
        {
            id, version, input.Code, input.NameAr, input.NameEn, input.CategoryId,
            input.BaseUnitId, input.ItemType, input.Notes, input.IsActive,
            CustomFields = JsonSerializer.Serialize(input.CustomFields)
        }, tx, cancellationToken: ct));

        if (updated is null)
        {
            await tx.RollbackAsync(ct);
            return null;
        }

        await ReplaceUnitsAsync(connection, tx, id, input, ct);
        await ReplaceBarcodesAsync(connection, tx, id, input.Barcodes, ct);
        await tx.CommitAsync(ct);
        return await GetAsync(id, ct);
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        await using var connection = await _factory.OpenAsync(ct);
        return await connection.ExecuteAsync(new CommandDefinition(
            "DELETE FROM items WHERE id = @id", new { id }, cancellationToken: ct)) > 0;
    }

    public async Task<Item?> SetActiveAsync(Guid id, bool isActive, CancellationToken ct = default)
    {
        await using var connection = await _factory.OpenAsync(ct);
        var rows = await connection.ExecuteAsync(new CommandDefinition(
            "UPDATE items SET is_active = @isActive WHERE id = @id", new { id, isActive }, cancellationToken: ct));
        return rows == 0 ? null : await GetAsync(id, ct);
    }

    private static async Task ReplaceUnitsAsync(NpgsqlConnection connection, NpgsqlTransaction tx,
        Guid itemId, ItemInput input, CancellationToken ct)
    {
        await connection.ExecuteAsync(new CommandDefinition(
            "DELETE FROM item_units WHERE item_id = @itemId", new { itemId }, tx, cancellationToken: ct));

        const string insert = """
            INSERT INTO item_units (item_id, unit_id, conversion_factor, sale_price, purchase_price, is_active)
            VALUES (@itemId, @UnitId, @ConversionFactor, @SalePrice, @PurchasePrice, @IsActive)
            """;

        var units = input.Units.Where(u => u.UnitId != Guid.Empty)
            .GroupBy(u => u.UnitId).Select(g => g.First()).ToList();
        if (units.All(u => u.UnitId != input.BaseUnitId))
            units.Insert(0, new ItemUnitInput { UnitId = input.BaseUnitId, ConversionFactor = 1 });

        foreach (var unit in units)
        {
            var factor = unit.UnitId == input.BaseUnitId ? 1m : unit.ConversionFactor;
            await connection.ExecuteAsync(new CommandDefinition(insert, new
            {
                itemId, unit.UnitId, ConversionFactor = factor,
                unit.SalePrice, unit.PurchasePrice, unit.IsActive
            }, tx, cancellationToken: ct));
        }
    }

    private static async Task ReplaceBarcodesAsync(NpgsqlConnection connection, NpgsqlTransaction tx,
        Guid itemId, IEnumerable<string> barcodes, CancellationToken ct)
    {
        await connection.ExecuteAsync(new CommandDefinition(
            "DELETE FROM item_barcodes WHERE item_id = @itemId", new { itemId }, tx, cancellationToken: ct));

        const string insert = """
            INSERT INTO item_barcodes (item_id, barcode) VALUES (@itemId, @barcode)
            """;
        foreach (var barcode in barcodes.Where(b => !string.IsNullOrWhiteSpace(b))
                     .Select(b => b.Trim()).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            await connection.ExecuteAsync(new CommandDefinition(insert, new { itemId, barcode },
                tx, cancellationToken: ct));
        }
    }
}
