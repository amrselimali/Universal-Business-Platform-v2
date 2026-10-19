using Ubp.Core.Models;
using Ubp.Core.Results;
using Ubp.Core.Security;
using Ubp.Infrastructure.Repositories;

namespace Ubp.Application.Services;

public sealed partial class RoleService
{
    public async Task<MutationOutcome> DeleteAsync(ActorContext actor, Guid id,
        CancellationToken ct = default)
    {
        await _permissions.RequireAsync(actor, ScreenCodes.Roles, ActionCodes.Delete, ct: ct);
        var old = await _repository.GetAsync(id, ct) ?? throw new NotFoundAppException();
        if (await _repository.CountUsersAsync(id, ct) > 0)
        {
            await _repository.SetActiveAsync(id, false, ct);
            await _audit.WriteAsync(actor, "roles", id.ToString(), "UPDATE", old, null, ct);
            return new MutationOutcome(false, true, "role.deactivated");
        }
        await _repository.DeleteAsync(id, ct);
        await _audit.WriteAsync(actor, "roles", id.ToString(), "DELETE", old, null, ct);
        PermissionService.InvalidateAll();
        return new MutationOutcome(true, false, "role.deleted");
    }

    public async Task<Role> SetActiveAsync(ActorContext actor, Guid id, bool isActive,
        CancellationToken ct = default)
    {
        await _permissions.RequireAsync(actor, ScreenCodes.Roles, ActionCodes.Edit, ct: ct);
        var old = await _repository.GetAsync(id, ct) ?? throw new NotFoundAppException();
        if (!await _repository.SetActiveAsync(id, isActive, ct)) throw new NotFoundAppException();
        PermissionService.InvalidateAll();
        var updated = await _repository.GetAsync(id, ct) ?? throw new NotFoundAppException();
        await _audit.WriteAsync(actor, "roles", id.ToString(), "UPDATE", old, updated, ct);
        return updated;
    }

    public async Task<Role> CopyPermissionsAsync(ActorContext actor, Guid sourceRoleId, Guid targetRoleId,
        CancellationToken ct = default)
    {
        await _permissions.RequireAsync(actor, ScreenCodes.Roles, ActionCodes.ManagePermissions, ct: ct);
        var target = await _repository.GetAsync(targetRoleId, ct) ?? throw new NotFoundAppException();
        var sourcePermissions = await _repository.GetPermissionsAsync(sourceRoleId, ct);
        var input = new RoleInput
        {
            Code = target.Code, NameAr = target.NameAr, NameEn = target.NameEn,
            IsActive = target.IsActive, Permissions = sourcePermissions.ToList()
        };
        await _repository.UpdateAsync(targetRoleId, target.Version, input, ct);
        PermissionService.InvalidateAll();
        await _audit.WriteAsync(actor, "role_permissions", targetRoleId.ToString(),
            "PERMISSION_CHANGE", null, sourcePermissions, ct);
        return (await _repository.GetAsync(targetRoleId, ct))!;
    }

    private static void Normalize(RoleInput input)
    {
        input.Code = (input.Code ?? "").Trim().ToUpperInvariant();
        input.NameAr = (input.NameAr ?? "").Trim();
        input.NameEn = string.IsNullOrWhiteSpace(input.NameEn) ? null : input.NameEn.Trim();
        input.Permissions = input.Permissions
            .Where(p => p.IsAllowed && AppRegistry.IsRegistered(p.ScreenCode, p.ActionCode))
            .GroupBy(p => (p.ScreenCode.ToUpperInvariant(), p.ActionCode.ToUpperInvariant()))
            .Select(g => new PermissionEntry
            {
                ScreenCode = g.Key.Item1, ActionCode = g.Key.Item2, IsAllowed = true,
                Scope = PermissionScope.Normalize(g.Max(x => PermissionScope.Normalize(x.Scope)))
            })
            .ToList();
    }

    private static void Validate(RoleInput input)
    {
        if (string.IsNullOrWhiteSpace(input.Code) || input.Code.Length > 50)
            throw new ValidationAppException("validation.code_required");
        if (string.IsNullOrWhiteSpace(input.NameAr))
            throw new ValidationAppException("validation.name_required");
    }
}
