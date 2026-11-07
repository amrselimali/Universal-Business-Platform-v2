using Ubp.Core.Results;
using Ubp.Core.Security;
using Ubp.Infrastructure.Repositories;

namespace Ubp.Application.Services;

/// <summary>مفضلة شاشات المستخدم لعرضها ضمن الصفحة الرئيسية — 0089.</summary>
public sealed class UserFavoriteService
{
    private readonly IUserFavoriteScreenRepository _repo;
    private readonly PermissionService _permissions;

    public UserFavoriteService(IUserFavoriteScreenRepository repo, PermissionService permissions)
    {
        _repo = repo; _permissions = permissions;
    }

    public async Task<IReadOnlyList<string>> ListAsync(ActorContext actor,
        CancellationToken ct = default)
    {
        await _permissions.RequireAsync(actor, ScreenCodes.Dashboard, ActionCodes.View, ct: ct);
        return await _repo.ListAsync(actor.UserId, ct);
    }

    /// <summary>يبدّل حالة المفضلة لشاشة (يضيف/يحذف). يعيد true إذا صارت مفضلة.</summary>
    public async Task<bool> ToggleAsync(ActorContext actor, string screenCode,
        CancellationToken ct = default)
    {
        await _permissions.RequireAsync(actor, ScreenCodes.Dashboard, ActionCodes.View, ct: ct);
        if (string.IsNullOrWhiteSpace(screenCode) || !AppRegistry.ByCode.ContainsKey(screenCode))
            throw new ValidationAppException("validation.required_fields");
        var visible = (await _permissions.GetVisibleScreenCodesAsync(actor)).ToHashSet();
        if (!visible.Contains(screenCode)) throw new ForbiddenException();

        var current = await _repo.ListAsync(actor.UserId, ct);
        if (current.Contains(screenCode))
        {
            await _repo.RemoveAsync(actor.UserId, screenCode, ct);
            return false;
        }
        await _repo.AddAsync(actor.UserId, screenCode, ct);
        return true;
    }
}
