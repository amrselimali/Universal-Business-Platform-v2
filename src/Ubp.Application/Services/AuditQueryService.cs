using Ubp.Core.Models;
using Ubp.Core.Paging;
using Ubp.Core.Results;
using Ubp.Core.Security;

namespace Ubp.Application.Services;

public sealed class AuditQueryService
{
    private readonly PermissionService _permissions;
    private readonly Infrastructure.Repositories.IAuditRepository _repository;
    private readonly Infrastructure.Repositories.IUserRepository _users;

    public AuditQueryService(PermissionService permissions,
        Infrastructure.Repositories.IAuditRepository repository,
        Infrastructure.Repositories.IUserRepository users)
    {
        _permissions = permissions; _repository = repository; _users = users;
    }

    public async Task<KeysetPage<AuditEntry>> ListAsync(ActorContext actor, AuditFilter filter,
        CancellationToken ct = default)
    {
        var scope = await _permissions.GetScopeAsync(actor, ScreenCodes.AuditLog, ActionCodes.View, ct);
        if (scope < 0) throw new ForbiddenException();
        return await _repository.ListAsync(filter, scope, actor.UserId, actor.AllowedBranchIds,
            actor.IsAdmin, 25, ct);
    }

    public async Task<IReadOnlyList<UserListItem>> GetFilterUsersAsync(ActorContext actor,
        CancellationToken ct = default)
    {
        var scope = await _permissions.GetScopeAsync(actor, ScreenCodes.AuditLog, ActionCodes.View, ct);
        if (scope < 0) throw new ForbiddenException();
        var result = await _users.ListAsync(new PageRequest { PageSize = 25 }, scope, actor.UserId,
            actor.AllowedBranchIds, actor.IsAdmin, actor.IsSuperAdmin, ct);
        return result.Items;
    }
}
