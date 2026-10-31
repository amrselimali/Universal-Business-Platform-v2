using Ubp.Core.Models;
using Ubp.Core.Results;
using Ubp.Core.Security;
using Ubp.Infrastructure.Repositories;

namespace Ubp.Application.Services;

public sealed partial class AssetService
{
    public async Task<IReadOnlyList<AssetCategory>> CategoriesAsync(ActorContext actor,
        CancellationToken ct = default)
    {
        await _permissions.RequireAsync(actor, ScreenCodes.Assets, ActionCodes.View, ct: ct);
        return await _assets.ListCategoriesAsync(ct);
    }

    public async Task<Guid> CreateCategoryAsync(ActorContext actor, AssetCategoryInput input,
        CancellationToken ct = default)
    {
        await _permissions.RequireAsync(actor, ScreenCodes.Assets, ActionCodes.Add, ct: ct);
        if (string.IsNullOrWhiteSpace(input.Code) || string.IsNullOrWhiteSpace(input.NameAr))
            throw new ValidationAppException("validation.category_code_required");
        if (input.DepreciationMethod == DepreciationMethods.Linear
            && (input.UsefulLifeMonths is null or <= 0))
            throw new ValidationAppException("validation.category_life_required");

        var id = await _assets.CreateCategoryAsync(input, actor.UserId, ct);
        await _audit.WriteAsync(actor, "asset_categories", id.ToString(), "INSERT", null,
            new { input.Code, input.DepreciationFrequency, input.UsefulLifeMonths }, ct);
        return id;
    }
}
