using Npgsql;
using Ubp.Core.Models;
using Ubp.Core.Paging;
using Ubp.Core.Results;
using Ubp.Core.Security;
using Ubp.Infrastructure.Repositories;

namespace Ubp.Application.Services;

public sealed class ItemCategoryService
{
    private readonly IItemCategoryRepository _repository;
    private readonly PermissionService _permissions;
    private readonly AuditService _audit;

    public ItemCategoryService(IItemCategoryRepository repository, PermissionService permissions,
        AuditService audit)
    {
        _repository = repository; _permissions = permissions; _audit = audit;
    }

    public async Task<PagedResult<ItemCategory>> ListAsync(ActorContext actor, PageRequest request,
        CancellationToken ct = default)
    {
        await _permissions.RequireAsync(actor, ScreenCodes.ItemCategories, ActionCodes.View, ct: ct);
        return await _repository.ListAsync(request, ct);
    }

    public async Task<IReadOnlyList<ItemCategory>> OptionsAsync(ActorContext actor,
        CancellationToken ct = default)
    {
        var allowed = await _permissions.HasAsync(actor, ScreenCodes.ItemCategories, ActionCodes.View, ct)
                   || await _permissions.HasAsync(actor, ScreenCodes.Items, ActionCodes.View, ct);
        if (!allowed) throw new ForbiddenException();
        return await _repository.ListOptionsAsync(ct);
    }

    public async Task<ItemCategory> CreateAsync(ActorContext actor, ItemCategoryInput input,
        CancellationToken ct = default)
    {
        await _permissions.RequireAsync(actor, ScreenCodes.ItemCategories, ActionCodes.Add, ct: ct);
        Normalize(input); Validate(input);
        await EnsureParentAsync(input, null, ct);
        try
        {
            var created = await _repository.CreateAsync(input, ct);
            await _audit.WriteAsync(actor, "item_categories", created.Id.ToString(), "INSERT", null, created, ct);
            return created;
        }
        catch (PostgresException ex) { throw DbErrorMapper.Map(ex); }
    }

    public async Task<ItemCategory> UpdateAsync(ActorContext actor, Guid id, int version,
        ItemCategoryInput input, CancellationToken ct = default)
    {
        await _permissions.RequireAsync(actor, ScreenCodes.ItemCategories, ActionCodes.Edit, ct: ct);
        Normalize(input); Validate(input);
        await EnsureParentAsync(input, id, ct);
        var old = await _repository.GetAsync(id, ct) ?? throw new NotFoundAppException();
        try
        {
            var updated = await _repository.UpdateAsync(id, version, input, ct)
                ?? throw new ConflictException("error.version_conflict");
            await _audit.WriteAsync(actor, "item_categories", id.ToString(), "UPDATE", old, updated, ct);
            return updated;
        }
        catch (PostgresException ex) { throw DbErrorMapper.Map(ex); }
    }

    public async Task<MutationOutcome> DeleteAsync(ActorContext actor, Guid id,
        CancellationToken ct = default)
    {
        await _permissions.RequireAsync(actor, ScreenCodes.ItemCategories, ActionCodes.Delete, ct: ct);
        var old = await _repository.GetAsync(id, ct) ?? throw new NotFoundAppException();
        if (await _repository.CountItemsAsync(id, ct) > 0 || await _repository.CountChildrenAsync(id, ct) > 0)
            throw new ValidationAppException("validation.category_in_use");

        try
        {
            if (!await _repository.DeleteAsync(id, ct)) throw new NotFoundAppException();
            await _audit.WriteAsync(actor, "item_categories", id.ToString(), "DELETE", old, null, ct);
            return new MutationOutcome(true, false, "common.deleted");
        }
        catch (PostgresException ex) { throw DbErrorMapper.Map(ex); }
    }

    public async Task<ItemCategory> SetActiveAsync(ActorContext actor, Guid id, bool isActive,
        CancellationToken ct = default)
    {
        await _permissions.RequireAsync(actor, ScreenCodes.ItemCategories, ActionCodes.ToggleActive, ct: ct);
        var old = await _repository.GetAsync(id, ct) ?? throw new NotFoundAppException();
        var updated = await _repository.SetActiveAsync(id, isActive, ct) ?? throw new NotFoundAppException();
        await _audit.WriteAsync(actor, "item_categories", id.ToString(), "UPDATE", old, updated, ct);
        return updated;
    }

    private async Task EnsureParentAsync(ItemCategoryInput input, Guid? currentId, CancellationToken ct)
    {
        if (!input.ParentId.HasValue) return;
        if (currentId.HasValue && input.ParentId == currentId)
            throw new ValidationAppException("validation.category_parent_invalid");

        var parent = await _repository.GetAsync(input.ParentId.Value, ct)
            ?? throw new ValidationAppException("validation.category_parent_invalid");

        // منع الدوران: لا يجوز أن يكون الأب سليلًا للتصنيف نفسه
        var cursor = parent;
        for (var depth = 0; depth < 20 && cursor.ParentId.HasValue; depth++)
        {
            if (currentId.HasValue && cursor.ParentId == currentId)
                throw new ValidationAppException("validation.category_parent_invalid");
            var next = await _repository.GetAsync(cursor.ParentId.Value, ct);
            if (next is null) break;
            cursor = next;
        }
    }

    private static void Normalize(ItemCategoryInput input)
    {
        input.Code = (input.Code ?? "").Trim().ToUpperInvariant();
        input.NameAr = (input.NameAr ?? "").Trim();
        input.NameEn = string.IsNullOrWhiteSpace(input.NameEn) ? null : input.NameEn.Trim();
    }

    private static void Validate(ItemCategoryInput input)
    {
        if (input.Code.Length == 0 || input.Code.Length > 30)
            throw new ValidationAppException("validation.code_required");
        if (input.NameAr.Length == 0 || input.NameAr.Length > 255)
            throw new ValidationAppException("validation.name_required");
    }
}
