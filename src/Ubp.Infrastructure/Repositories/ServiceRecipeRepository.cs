using Dapper;
using Ubp.Core.Models;
using Ubp.Core.Paging;
using Ubp.Infrastructure.Data;

namespace Ubp.Infrastructure.Repositories;

public interface IServiceRecipeRepository
{
    Task<PagedResult<ServiceRecipe>> ListAsync(PageRequest request, bool includeInactive, CancellationToken ct = default);
    Task<ServiceRecipe?> GetAsync(Guid id, CancellationToken ct = default);
    Task<ServiceRecipe?> GetByItemAsync(Guid itemId, CancellationToken ct = default);
    Task<Guid> UpsertAsync(Guid? id, ServiceRecipeInput input, Guid userId, CancellationToken ct = default);
    Task<ServiceRecipe?> SetActiveAsync(Guid id, bool isActive, Guid userId, CancellationToken ct = default);
    Task<bool> DeleteAsync(Guid id, CancellationToken ct = default);
}

public sealed class ServiceRecipeRepository : RepositoryBase, IServiceRecipeRepository
{
    private const string Columns = """
        r.id AS Id, r.item_id AS ItemId, i.code AS ItemCode, i.name_ar AS ItemName,
        r.notes AS Notes, r.is_active AS IsActive, r.version AS Version FROM service_recipes r
        JOIN items i ON i.id = r.item_id
        """;

    public ServiceRecipeRepository(IDbConnectionFactory factory) : base(factory) { }

    public async Task<PagedResult<ServiceRecipe>> ListAsync(PageRequest request, bool includeInactive,
        CancellationToken ct = default)
    {
        var where = includeInactive ? "WHERE true" : "WHERE r.is_active";
        if (!string.IsNullOrWhiteSpace(request.Search))
            where += " AND (i.code ILIKE '%' || @search || '%' OR normalize_search(i.name_ar) " +
                     "LIKE '%' || normalize_search(@search) || '%')";
        return await PageAsync<ServiceRecipe>(
            $"SELECT {Columns} {where} ORDER BY i.code LIMIT @take OFFSET @offset",
            $"SELECT count(*)::int FROM service_recipes r JOIN items i ON i.id = r.item_id {where}",
            new { search = request.Search, take = request.SafePageSize, offset = request.Offset },
            request, ct);
    }

    public async Task<ServiceRecipe?> GetAsync(Guid id, CancellationToken ct = default)
    {
        var header = await SingleAsync<ServiceRecipe>(
            $"SELECT {Columns} WHERE r.id = @id", new { id }, ct);
        if (header is null) return null;

        var items = await QueryAsync<ServiceRecipeItem>("""
            SELECT x.id AS Id, x.recipe_id AS RecipeId, x.line_no AS LineNo,
                   x.component_item_id AS ComponentItemId, i.code AS ComponentCode,
                   i.name_ar AS ComponentName, x.unit_id AS UnitId, u.name_ar AS UnitName,
                   x.quantity AS Quantity, x.consumption_mode AS ConsumptionMode,
                   x.is_required AS IsRequired, x.notes AS Notes
            FROM service_recipe_items x
            JOIN items i ON i.id = x.component_item_id
            JOIN units u ON u.id = x.unit_id
            WHERE x.recipe_id = @id ORDER BY x.line_no
            """, new { id }, ct);

        var substitutes = await QueryAsync<SubstituteRow>(
            "SELECT recipe_item_id AS RecipeItemId, substitute_item_id AS ItemId FROM recipe_substitutes",
            null, ct);
        var map = substitutes.GroupBy(s => s.RecipeItemId)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<Guid>)g.Select(x => x.ItemId).ToList());

        foreach (var item in items)
            item.SubstituteItemIds = map.TryGetValue(item.Id, out var list) ? list : Array.Empty<Guid>();

        header.Items = items;
        return header;
    }

    public async Task<ServiceRecipe?> GetByItemAsync(Guid itemId, CancellationToken ct = default)
    {
        var id = await SingleAsync<Guid?>(
            "SELECT id FROM service_recipes WHERE item_id = @itemId", new { itemId }, ct);
        return id is null ? null : await GetAsync(id.Value, ct);
    }

    private sealed class SubstituteRow
    {
        public Guid RecipeItemId { get; set; }
        public Guid ItemId { get; set; }
    }

    /// <summary>حفظ الوصفة كاملة (رأس + بنود + بدائل) في معاملة واحدة.</summary>
    public async Task<Guid> UpsertAsync(Guid? id, ServiceRecipeInput input, Guid userId,
        CancellationToken ct = default)
    {
        await using var connection = await Factory.OpenAsync(ct);
        await using var tx = await connection.BeginTransactionAsync(ct);

        var recipeId = id ?? Guid.CreateVersion7();
        if (id is null)
        {
            await connection.ExecuteAsync(new CommandDefinition("""
                INSERT INTO service_recipes (id, item_id, notes, is_active, created_by)
                VALUES (@recipeId, @ItemId, @Notes, @IsActive, @userId)
                """, new { recipeId, input.ItemId, input.Notes, input.IsActive, userId },
                tx, cancellationToken: ct));
        }
        else
        {
            var rows = await connection.ExecuteAsync(new CommandDefinition("""
                UPDATE service_recipes SET notes = @Notes, is_active = @IsActive, updated_by = @userId
                WHERE id = @recipeId
                """, new { recipeId, input.Notes, input.IsActive, userId }, tx, cancellationToken: ct));
            if (rows == 0)
            {
                await tx.RollbackAsync(ct);
                throw new Ubp.Core.Results.NotFoundAppException();
            }
        }

        await connection.ExecuteAsync(new CommandDefinition(
            "DELETE FROM service_recipe_items WHERE recipe_id = @recipeId",
            new { recipeId }, tx, cancellationToken: ct));

        var lineNo = 0;
        foreach (var item in input.Items)
        {
            lineNo++;
            var itemId = Guid.CreateVersion7();
            await connection.ExecuteAsync(new CommandDefinition("""
                INSERT INTO service_recipe_items (id, recipe_id, line_no, component_item_id,
                    unit_id, quantity, consumption_mode, is_required, notes)
                VALUES (@itemId, @recipeId, @lineNo, @ComponentItemId, @UnitId, @Quantity,
                        @ConsumptionMode, @IsRequired, @Notes)
                """, new { itemId, recipeId, lineNo, item.ComponentItemId, item.UnitId,
                    item.Quantity, item.ConsumptionMode, item.IsRequired, item.Notes },
                tx, cancellationToken: ct));

            foreach (var substitute in item.SubstituteItemIds.Distinct())
            {
                await connection.ExecuteAsync(new CommandDefinition("""
                    INSERT INTO recipe_substitutes (id, recipe_item_id, substitute_item_id)
                    VALUES (@id, @itemId, @substitute)
                    ON CONFLICT (recipe_item_id, substitute_item_id) DO NOTHING
                    """, new { id = Guid.CreateVersion7(), itemId, substitute }, tx,
                    cancellationToken: ct));
            }
        }

        await tx.CommitAsync(ct);
        return recipeId;
    }

    public async Task<ServiceRecipe?> SetActiveAsync(Guid id, bool isActive, Guid userId,
        CancellationToken ct = default)
    {
        var rows = await ExecuteRowAsync(
            "UPDATE service_recipes SET is_active = @isActive, updated_by = @userId WHERE id = @id",
            new { id, isActive, userId }, ct);
        return rows == 0 ? null : await GetAsync(id, ct);
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken ct = default) =>
        await ExecuteRowAsync("DELETE FROM service_recipes WHERE id = @id", new { id }, ct) > 0;
}
