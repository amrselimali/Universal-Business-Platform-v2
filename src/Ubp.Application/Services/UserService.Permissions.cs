using Ubp.Core.Models;
using Ubp.Core.Results;
using Ubp.Core.Security;

namespace Ubp.Application.Services;

public sealed partial class UserService
{
    public async Task<IReadOnlyList<EffectivePermission>> GetPermissionMatrixAsync(
        ActorContext actor, Guid targetUserId, CancellationToken ct = default)
    {
        await _permissions.RequireAsync(actor, ScreenCodes.Users, ActionCodes.View, ct: ct);
        var target = await _users.GetDetailAsync(targetUserId, ct) ?? throw new NotFoundAppException();
        EnsureTargetVisible(actor, target.User);
        return await _permissions.GetMatrixAsync(actor, targetUserId, target.User.IsAdmin, ct);
    }

    public async Task SavePermissionsAsync(ActorContext actor, Guid targetUserId,
        IEnumerable<PermissionEntry> entries, CancellationToken ct = default)
    {
        await _permissions.RequireAsync(actor, ScreenCodes.Users, ActionCodes.ManagePermissions, ct: ct);
        var target = await _users.GetDetailAsync(targetUserId, ct) ?? throw new NotFoundAppException();
        EnsureTargetVisible(actor, target.User);

        var desired = entries
            .Where(p => AppRegistry.IsRegistered(p.ScreenCode, p.ActionCode))
            .GroupBy(p => (Screen: p.ScreenCode.ToUpperInvariant(), Action: p.ActionCode.ToUpperInvariant()))
            .Select(g => g.Last())
            .Select(p => new PermissionEntry
            {
                ScreenCode = p.ScreenCode.ToUpperInvariant(),
                ActionCode = p.ActionCode.ToUpperInvariant(),
                IsAllowed = p.IsAllowed,
                Scope = PermissionScope.Normalize(p.Scope)
            })
            .ToList();

        var roleGrants = await _permissionRepo.GetRoleGrantsForUserAsync(targetUserId, ct);
        var baseline = roleGrants
            .GroupBy(p => (p.ScreenCode.ToUpperInvariant(), p.ActionCode.ToUpperInvariant()))
            .ToDictionary(g => g.Key, g => new PermissionEntry
            {
                ScreenCode = g.Key.Item1, ActionCode = g.Key.Item2,
                IsAllowed = g.Any(x => x.IsAllowed),
                Scope = g.Max(x => PermissionScope.Normalize(x.Scope))
            });

        var overrides = desired.Where(p =>
            !baseline.TryGetValue((p.ScreenCode, p.ActionCode), out var role) ||
            role.IsAllowed != p.IsAllowed ||
            (p.IsAllowed && role.Scope != p.Scope)).ToList();

        var previous = await _permissionRepo.GetUserOverridesAsync(targetUserId, ct);
        await _permissionRepo.ReplaceUserPermissionsAsync(targetUserId, overrides, ct);
        PermissionService.InvalidateAll();
        await _audit.WriteAsync(actor, "user_permissions", targetUserId.ToString(),
            "PERMISSION_CHANGE", previous, overrides, ct);
    }

    public async Task CopyPermissionsAsync(ActorContext actor, Guid sourceUserId, Guid targetUserId,
        CancellationToken ct = default)
    {
        await _permissions.RequireAsync(actor, ScreenCodes.Users, ActionCodes.ManagePermissions, ct: ct);
        var source = await _users.GetDetailAsync(sourceUserId, ct) ?? throw new NotFoundAppException();
        var target = await _users.GetDetailAsync(targetUserId, ct) ?? throw new NotFoundAppException();
        EnsureTargetVisible(actor, source.User);
        EnsureTargetVisible(actor, target.User);

        var previous = await _permissionRepo.GetUserOverridesAsync(targetUserId, ct);
        await _permissionRepo.CopyUserPermissionsAsync(sourceUserId, targetUserId, ct);
        PermissionService.InvalidateAll();
        var current = await _permissionRepo.GetUserOverridesAsync(targetUserId, ct);
        await _audit.WriteAsync(actor, "user_permissions", targetUserId.ToString(),
            "PERMISSION_CHANGE", previous, current, ct);
    }
}
