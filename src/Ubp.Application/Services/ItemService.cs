using Npgsql;
using Ubp.Core.Models;
using Ubp.Core.Paging;
using Ubp.Core.Results;
using Ubp.Core.Security;
using Ubp.Infrastructure.Repositories;

namespace Ubp.Application.Services;

public sealed class ItemService
{
    private readonly IItemRepository _items;
    private readonly IUnitRepository _units;
    private readonly IItemCategoryRepository _categories;
    private readonly CustomFieldService _customFields;
    private readonly PermissionService _permissions;
    private readonly AuditService _audit;

    public ItemService(IItemRepository items, IUnitRepository units,
        IItemCategoryRepository categories, CustomFieldService customFields,
        PermissionService permissions, AuditService audit)
    {
        _items = items; _units = units; _categories = categories;
        _customFields = customFields; _permissions = permissions; _audit = audit;
    }

    public async Task<PagedResult<Item>> ListAsync(ActorContext actor, PageRequest request,
        Guid? categoryId, string? itemType, CancellationToken ct = default)
    {
        await _permissions.RequireAsync(actor, ScreenCodes.Items, ActionCodes.View, ct: ct);
        return await _items.ListAsync(request, categoryId, itemType, ct);
    }

    public async Task<IReadOnlyList<ItemOption>> OptionsAsync(ActorContext actor, string? search,
        bool sellableOnly = false, CancellationToken ct = default)
    {
        await _permissions.RequireAsync(actor, ScreenCodes.Items, ActionCodes.View, ct: ct);
        return await _items.ListOptionsAsync(search, 25, sellableOnly, ct);
    }

    public async Task<ItemDetail> GetDetailAsync(ActorContext actor, Guid id, CancellationToken ct = default)
    {
        await _permissions.RequireAsync(actor, ScreenCodes.Items, ActionCodes.View, ct: ct);
        return await _items.GetDetailAsync(id, ct) ?? throw new NotFoundAppException();
    }

    public async Task<Item> CreateAsync(ActorContext actor, ItemInput input, CancellationToken ct = default)
    {
        await _permissions.RequireAsync(actor, ScreenCodes.Items, ActionCodes.Add, ct: ct);
        await NormalizeAndValidateAsync(input, null, ct);
        try
        {
            var created = await _items.CreateAsync(input, ct);
            await _audit.WriteAsync(actor, "items", created.Id.ToString(), "INSERT", null, created, ct);
            return created;
        }
        catch (PostgresException ex) { throw DbErrorMapper.Map(ex); }
    }

    public async Task<Item> UpdateAsync(ActorContext actor, Guid id, int version, ItemInput input,
        CancellationToken ct = default)
    {
        await _permissions.RequireAsync(actor, ScreenCodes.Items, ActionCodes.Edit, ct: ct);
        var old = await _items.GetAsync(id, ct) ?? throw new NotFoundAppException();
        await NormalizeAndValidateAsync(input, old, ct);

        if (old.BaseUnitId != input.BaseUnitId && await _items.HasStockAsync(id, ct))
            throw new ValidationAppException("validation.item_base_unit_locked");

        try
        {
            var updated = await _items.UpdateAsync(id, version, input, ct)
                ?? throw new ConflictException("error.version_conflict");
            await _audit.WriteAsync(actor, "items", id.ToString(), "UPDATE", old, updated, ct);
            return updated;
        }
        catch (PostgresException ex) { throw DbErrorMapper.Map(ex); }
    }

    public async Task<MutationOutcome> DeleteAsync(ActorContext actor, Guid id, CancellationToken ct = default)
    {
        await _permissions.RequireAsync(actor, ScreenCodes.Items, ActionCodes.Delete, ct: ct);
        var old = await _items.GetAsync(id, ct) ?? throw new NotFoundAppException();
        if (await _items.HasStockAsync(id, ct))
            throw new ValidationAppException("validation.item_in_use");

        try
        {
            if (!await _items.DeleteAsync(id, ct)) throw new NotFoundAppException();
            await _audit.WriteAsync(actor, "items", id.ToString(), "DELETE", old, null, ct);
            return new MutationOutcome(true, false, "common.deleted");
        }
        catch (PostgresException ex) { throw DbErrorMapper.Map(ex); }
    }

    public async Task<Item> SetActiveAsync(ActorContext actor, Guid id, bool isActive,
        CancellationToken ct = default)
    {
        await _permissions.RequireAsync(actor, ScreenCodes.Items, ActionCodes.ToggleActive, ct: ct);
        var old = await _items.GetAsync(id, ct) ?? throw new NotFoundAppException();
        var updated = await _items.SetActiveAsync(id, isActive, ct) ?? throw new NotFoundAppException();
        await _audit.WriteAsync(actor, "items", id.ToString(), "UPDATE", old, updated, ct);
        return updated;
    }

    private async Task NormalizeAndValidateAsync(ItemInput input, Item? old, CancellationToken ct)
    {
        input.Code = (input.Code ?? "").Trim().ToUpperInvariant();
        input.NameAr = (input.NameAr ?? "").Trim();
        input.NameEn = string.IsNullOrWhiteSpace(input.NameEn) ? null : input.NameEn.Trim();
        input.Notes = string.IsNullOrWhiteSpace(input.Notes) ? null : input.Notes.Trim();
        input.ItemType = string.Equals(input.ItemType, "SERVICE", StringComparison.OrdinalIgnoreCase)
            ? "SERVICE" : "GOODS";

        if (input.Code.Length == 0 || input.Code.Length > 50)
            throw new ValidationAppException("validation.code_required");
        if (input.NameAr.Length == 0 || input.NameAr.Length > 255)
            throw new ValidationAppException("validation.name_required");
        if (input.BaseUnitId == Guid.Empty)
            throw new ValidationAppException("validation.unit_required");

        var baseUnit = await _units.GetAsync(input.BaseUnitId, ct)
            ?? throw new ValidationAppException("validation.unit_not_found");
        if (!baseUnit.IsActive)
            throw new ValidationAppException("validation.unit_inactive");

        if (input.CategoryId.HasValue)
        {
            var category = await _categories.GetAsync(input.CategoryId.Value, ct)
                ?? throw new ValidationAppException("validation.category_not_found");
            if (!category.IsActive)
                throw new ValidationAppException("validation.category_inactive");
        }

        input.Units = (input.Units ?? new List<ItemUnitInput>())
            .Where(u => u.UnitId != Guid.Empty)
            .GroupBy(u => u.UnitId).Select(g => g.First()).ToList();
        var baseRow = input.Units.FirstOrDefault(u => u.UnitId == input.BaseUnitId);
        if (baseRow is null)
            input.Units.Insert(0, new ItemUnitInput { UnitId = input.BaseUnitId, ConversionFactor = 1 });
        else
            baseRow.ConversionFactor = 1;

        foreach (var unit in input.Units)
        {
            if (unit.ConversionFactor <= 0)
                throw new ValidationAppException("validation.conversion_factor_invalid");
            if (unit.SalePrice is < 0 || unit.PurchasePrice is < 0)
                throw new ValidationAppException("validation.price_invalid");
            if (unit.UnitId != input.BaseUnitId)
            {
                var exists = await _units.GetAsync(unit.UnitId, ct)
                    ?? throw new ValidationAppException("validation.unit_not_found");
                if (!exists.IsActive)
                    throw new ValidationAppException("validation.unit_inactive");
            }
        }

        input.Barcodes = (input.Barcodes ?? new List<string>())
            .Select(b => (b ?? "").Trim())
            .Where(b => b.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (input.Barcodes.Any(b => b.Length > 64))
            throw new ValidationAppException("validation.barcode_invalid");

        input.CustomFields = await _customFields.ValidateValuesAsync("items", input.CustomFields, ct);
    }
}
