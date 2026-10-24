using Npgsql;
using Ubp.Core.Models;
using Ubp.Core.Paging;
using Ubp.Core.Results;
using Ubp.Core.Security;
using Ubp.Infrastructure.Repositories;

namespace Ubp.Application.Services;

/// <summary>وصفات الخدمات وإعدادات الفرع (D14/D15/N10).</summary>
public sealed class RecipeService
{
    private readonly IServiceRecipeRepository _recipes;
    private readonly IItemRepository _items;
    private readonly IBranchSettingsRepository _branchSettings;
    private readonly IWarehouseRepository _warehouses;
    private readonly PermissionService _permissions;
    private readonly AuditService _audit;

    public RecipeService(IServiceRecipeRepository recipes, IItemRepository items,
        IBranchSettingsRepository branchSettings, IWarehouseRepository warehouses,
        PermissionService permissions, AuditService audit)
    {
        _recipes = recipes; _items = items; _branchSettings = branchSettings;
        _warehouses = warehouses; _permissions = permissions; _audit = audit;
    }

    public async Task<PagedResult<ServiceRecipe>> ListAsync(ActorContext actor, PageRequest request,
        bool includeInactive, CancellationToken ct = default)
    {
        await _permissions.RequireAsync(actor, ScreenCodes.ServiceRecipes, ActionCodes.View, ct: ct);
        return await _recipes.ListAsync(request, includeInactive, ct);
    }

    public async Task<ServiceRecipe> GetAsync(ActorContext actor, Guid id,
        CancellationToken ct = default)
    {
        await _permissions.RequireAsync(actor, ScreenCodes.ServiceRecipes, ActionCodes.View, ct: ct);
        return await _recipes.GetAsync(id, ct)
            ?? await GetByItemAsync(actor, id, ct);
    }

    public async Task<ServiceRecipe?> GetByItemAsync(ActorContext actor, Guid itemId,
        CancellationToken ct = default)
    {
        await _permissions.RequireAsync(actor, ScreenCodes.ServiceRecipes, ActionCodes.View, ct: ct);
        return await _recipes.GetByItemAsync(itemId, ct);
    }

    /// <summary>حفظ الوصفة: الخدمة يجب أن تكون صنفاً من نوع SERVICE.</summary>
    public async Task<ServiceRecipe> SaveAsync(ActorContext actor, Guid? id, ServiceRecipeInput input,
        CancellationToken ct = default)
    {
        await _permissions.RequireAsync(actor, ScreenCodes.ServiceRecipes,
            id is null ? ActionCodes.Create : ActionCodes.EditLines, ct: ct);

        var item = await _items.GetAsync(input.ItemId, ct) ?? throw new NotFoundAppException();
        if (item.ItemType != "SERVICE")
            throw new ValidationAppException("validation.recipe_requires_service_item");
        if (input.Items.Count == 0)
            throw new ValidationAppException("validation.recipe_has_no_lines");

        foreach (var line in input.Items)
        {
            if (line.ComponentItemId == input.ItemId)
                throw new ValidationAppException("validation.recipe_self_reference");
            if (line.Quantity <= 0)
                throw new ValidationAppException("validation.line_amount");
            if (line.ConsumptionMode is < 0 or > 2)
                throw new ValidationAppException("validation.consumption_mode");
        }

        try
        {
            var recipeId = await _recipes.UpsertAsync(id, input, actor.UserId, ct);
            var saved = await _recipes.GetAsync(recipeId, ct) ?? throw new NotFoundAppException();
            await _audit.WriteAsync(actor, "service_recipes", recipeId.ToString(),
                id is null ? "INSERT" : "UPDATE", null, saved, ct);
            return saved;
        }
        catch (PostgresException ex) { throw DbErrorMapper.Map(ex); }
    }

    public async Task<ServiceRecipe> SetActiveAsync(ActorContext actor, Guid id, bool isActive,
        CancellationToken ct = default)
    {
        await _permissions.RequireAsync(actor, ScreenCodes.ServiceRecipes, ActionCodes.ToggleActive,
            ct: ct);
        var updated = await _recipes.SetActiveAsync(id, isActive, actor.UserId, ct)
            ?? throw new NotFoundAppException();
        await _audit.WriteAsync(actor, "service_recipes", id.ToString(), "UPDATE", null, updated, ct);
        return updated;
    }

    // ---------------- إعدادات الفرع: مخزن المستهلكات ----------------
    public async Task<BranchSettings?> GetBranchSettingsAsync(ActorContext actor, Guid branchId,
        CancellationToken ct = default)
    {
        await _permissions.RequireAsync(actor, ScreenCodes.Branches, ActionCodes.View, ct: ct);
        return await _branchSettings.GetAsync(branchId, ct);
    }

    public async Task<IReadOnlyList<Warehouse>> ConsumableWarehousesAsync(ActorContext actor,
        Guid branchId, CancellationToken ct = default)
    {
        await _permissions.RequireAsync(actor, ScreenCodes.Branches, ActionCodes.View, ct: ct);
        var all = await _warehouses.ListOptionsAsync(PermissionScope.All, actor.UserId,
            actor.AllowedBranchIds, actor.IsAdmin, ct);
        return all.Where(w => w.WarehouseType == WarehouseTypes.Consumables &&
                              (w.BranchIds.Count == 0 || w.BranchIds.Contains(branchId)))
            .ToList();
    }

    public async Task<BranchSettings> SaveBranchSettingsAsync(ActorContext actor, Guid branchId,
        Guid? consumablesWarehouseId, CancellationToken ct = default)
    {
        await _permissions.RequireAsync(actor, ScreenCodes.Branches, ActionCodes.ManageDefaults, ct: ct);
        try
        {
            var saved = await _branchSettings.UpsertAsync(branchId, consumablesWarehouseId,
                actor.UserId, ct);
            await _audit.WriteAsync(actor, "branch_settings", branchId.ToString(), "UPDATE", null,
                saved, ct);
            return saved;
        }
        catch (PostgresException ex) { throw DbErrorMapper.Map(ex); }
    }
}
