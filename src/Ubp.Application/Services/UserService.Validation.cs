using Ubp.Core.Models;
using Ubp.Core.Results;
using Ubp.Core.Security;

namespace Ubp.Application.Services;

public sealed partial class UserService
{
    private async Task ValidateCoreAsync(ActorContext actor, UserInput input, Guid? id,
        CancellationToken ct)
    {
        if (!UsernamePattern.IsMatch(input.Username))
            throw new ValidationAppException("validation.username_invalid");
        if (await _users.IsUsernameTakenAsync(input.Username, id, ct))
            throw new ValidationAppException("validation.username_taken");
        if (input.IsSuperAdmin && !input.IsAdmin)
            throw new ValidationAppException("validation.super_admin_requires_admin");
        if ((input.IsAdmin || input.IsSuperAdmin) && !actor.IsSuperAdmin)
            throw new ForbiddenException("error.admin_flag_forbidden");
        if (!input.IsAdmin && input.BranchIds.Count == 0)
            throw new ValidationAppException("validation.user_branch_required");
    }

    private async Task RequireLinkPermissionsAsync(ActorContext actor, UserInput input,
        UserDetail? old, CancellationToken ct)
    {
        var branchesChanged = old is null
            ? input.BranchIds.Count > 0
            : !old.BranchIds.OrderBy(x => x).SequenceEqual(input.BranchIds.Distinct().OrderBy(x => x));
        var warehousesChanged = old is null
            ? input.WarehouseIds.Count > 0
            : !old.WarehouseIds.OrderBy(x => x).SequenceEqual(input.WarehouseIds.Distinct().OrderBy(x => x));
        var rolesChanged = old is null
            ? input.RoleIds.Count > 0
            : !old.RoleIds.OrderBy(x => x).SequenceEqual(input.RoleIds.Distinct().OrderBy(x => x));

        if (branchesChanged)
            await _permissions.RequireAsync(actor, ScreenCodes.Users, ActionCodes.ManageBranches, ct: ct);
        if (warehousesChanged)
            await _permissions.RequireAsync(actor, ScreenCodes.Users, ActionCodes.ManageWarehouses, ct: ct);
        if (rolesChanged)
            await _permissions.RequireAsync(actor, ScreenCodes.Users, ActionCodes.ManageRoles, ct: ct);
    }

    private async Task ValidateRelationsAsync(ActorContext actor, UserInput input,
        CancellationToken ct)
    {
        var branchIds = input.BranchIds.Distinct().ToList();
        foreach (var branchId in branchIds)
        {
            if (!actor.IsAdmin && !actor.CanAccessBranch(branchId))
                throw new ForbiddenException("error.branch_not_allowed");
            if (await _branches.GetAsync(branchId, ct) is null)
                throw new ValidationAppException("validation.branch_not_found");
        }

        if (!input.IsAdmin)
        {
            foreach (var warehouseId in input.WarehouseIds.Distinct())
            {
                var warehouse = await _warehouses.GetAsync(warehouseId, ct)
                    ?? throw new ValidationAppException("validation.warehouse_not_found");
                if (!warehouse.BranchIds.Intersect(branchIds).Any())
                    throw new ValidationAppException("validation.warehouse_not_in_branches",
                        new Dictionary<string, string> { ["warehouses"] = warehouse.NameAr });
            }
        }

        var roles = await _roles.ListOptionsAsync(ct);
        var roleIds = roles.Select(r => r.Id).ToHashSet();
        if (input.RoleIds.Any(r => !roleIds.Contains(r)))
            throw new ValidationAppException("validation.role_not_found");
    }
}
