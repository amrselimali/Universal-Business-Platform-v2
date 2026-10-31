using Ubp.Core.Models;
using Ubp.Core.Paging;
using Ubp.Core.Results;
using Ubp.Core.Security;
using Ubp.Infrastructure.Repositories;

namespace Ubp.Application.Services;

/// <summary>
/// الأصول الثابتة والعدادات والصيانة (8أ): الفرع NULL = أصل مشترك.
/// قراءة العدّاد تصاعدية، وأمر الصيانة يُصفّر العدّاد فقط إذا كان الأصل مُعدّاً لذلك.
/// </summary>
public sealed partial class AssetService
{
    private readonly IAssetRepository _assets;
    private readonly IAccountRepository _accounts;
    private readonly PermissionService _permissions;
    private readonly AuditService _audit;

    public AssetService(IAssetRepository assets, IAccountRepository accounts,
        PermissionService permissions, AuditService audit)
    {
        _assets = assets; _accounts = accounts; _permissions = permissions; _audit = audit;
    }

    private static Guid[] Scope(ActorContext actor) =>
        actor.IsSuperAdmin ? Array.Empty<Guid>() : actor.AllowedBranchIds.Distinct().ToArray();

    public async Task<PagedResult<FixedAsset>> ListAsync(ActorContext actor, PageRequest request,
        string? search = null, string? status = null, bool meterOnly = false,
        CancellationToken ct = default)
    {
        await _permissions.RequireAsync(actor, ScreenCodes.Assets, ActionCodes.View, ct: ct);
        return await _assets.ListAsync(request, Scope(actor), search, status, meterOnly, ct);
    }

    public async Task<FixedAsset> GetAsync(ActorContext actor, Guid id, CancellationToken ct = default)
    {
        await _permissions.RequireAsync(actor, ScreenCodes.Assets, ActionCodes.View, ct: ct);
        var asset = await _assets.GetAsync(id, ct) ?? throw new NotFoundAppException();
        EnsureVisible(actor, asset);
        return asset;
    }

    public async Task<FixedAsset> CreateAsync(ActorContext actor, AssetInput input,
        CancellationToken ct = default)
    {
        await _permissions.RequireAsync(actor, ScreenCodes.Assets, ActionCodes.Add, ct: ct);
        if (input.BranchId is { } branch) BranchScope.Ensure(actor, branch);
        Validate(input);

        var id = await _assets.CreateAsync(input, actor.UserId, ct);
        await _audit.WriteAsync(actor, "fixed_assets", id.ToString(), "INSERT", null,
            new { input.Code, input.Cost, input.BranchId, input.IsMetered }, ct);
        return await GetAsync(actor, id, ct);
    }

    public async Task<FixedAsset> UpdateAsync(ActorContext actor, Guid id, int version,
        AssetInput input, CancellationToken ct = default)
    {
        await _permissions.RequireAsync(actor, ScreenCodes.Assets, ActionCodes.Edit, ct: ct);
        var existing = await _assets.GetAsync(id, ct) ?? throw new NotFoundAppException();
        EnsureVisible(actor, existing);
        if (input.BranchId is { } branch) BranchScope.Ensure(actor, branch);
        Validate(input);

        var updated = await _assets.UpdateAsync(id, version, input, actor.UserId, ct)
            ?? throw new ConflictException("error.version_conflict");
        await _audit.WriteAsync(actor, "fixed_assets", id.ToString(), "UPDATE", existing, updated, ct);
        return updated;
    }

    /// <summary>تغيير الحالة (نشط/متوقف). الاستبعاد الكامل يحتاج قيداً محاسبياً — الخطوة التالية.</summary>
    public async Task SetStatusAsync(ActorContext actor, Guid id, string status,
        CancellationToken ct = default)
    {
        await _permissions.RequireAsync(actor, ScreenCodes.Assets, ActionCodes.Edit, ct: ct);
        if (status is not (AssetStatuses.Active or AssetStatuses.Idle))
            throw new ValidationAppException("validation.asset_status_invalid");
        var asset = await _assets.GetAsync(id, ct) ?? throw new NotFoundAppException();
        EnsureVisible(actor, asset);
        if (!await _assets.SetStatusAsync(id, status, actor.UserId, ct))
            throw new ValidationAppException("validation.asset_disposed");

        await _audit.WriteAsync(actor, "fixed_assets", id.ToString(), "UPDATE",
            new { asset.Status }, new { status }, ct);
    }

    private static void Validate(AssetInput input)
    {
        if (string.IsNullOrWhiteSpace(input.Code) || string.IsNullOrWhiteSpace(input.NameAr))
            throw new ValidationAppException("validation.asset_code_required");
        if (input.Cost < 0 || input.SalvageValue < 0 || input.SalvageValue > input.Cost)
            throw new ValidationAppException("validation.asset_cost_invalid");
    }

    private static void EnsureVisible(ActorContext actor, FixedAsset asset)
    {
        if (asset.IsShared) return;
        BranchScope.Ensure(actor, asset.BranchId!.Value);
    }
}
