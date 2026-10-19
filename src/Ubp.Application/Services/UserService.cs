using System.Text.RegularExpressions;
using Ubp.Core.Models;
using Ubp.Core.Paging;
using Ubp.Core.Results;
using Ubp.Core.Security;
using Ubp.Infrastructure.Repositories;
using Ubp.Infrastructure.Security;

namespace Ubp.Application.Services;

public sealed partial class UserService
{
    internal static readonly Regex UsernamePattern = new("^[a-zA-Z0-9._@-]{3,150}$", RegexOptions.Compiled);

    private readonly IUserRepository _users;
    private readonly IBranchRepository _branches;
    private readonly IWarehouseRepository _warehouses;
    private readonly IRoleRepository _roles;
    private readonly IPermissionRepository _permissionRepo;
    private readonly ISessionRepository _sessions;
    private readonly IRefreshTokenRepository _refreshTokens;
    private readonly PermissionService _permissions;
    private readonly ISettingsService _settings;
    private readonly PasswordHasher _hasher;
    private readonly AuditService _audit;

    public UserService(IUserRepository users, IBranchRepository branches,
        IWarehouseRepository warehouses, IRoleRepository roles, IPermissionRepository permissionRepo,
        ISessionRepository sessions, IRefreshTokenRepository refreshTokens,
        PermissionService permissions, ISettingsService settings, PasswordHasher hasher, AuditService audit)
    {
        _users = users; _branches = branches; _warehouses = warehouses; _roles = roles;
        _permissionRepo = permissionRepo; _sessions = sessions; _refreshTokens = refreshTokens;
        _permissions = permissions; _settings = settings; _hasher = hasher; _audit = audit;
    }

    public async Task<PagedResult<UserListItem>> ListAsync(ActorContext actor, PageRequest request,
        CancellationToken ct = default)
    {
        var scope = await _permissions.GetScopeAsync(actor, ScreenCodes.Users, ActionCodes.View, ct);
        if (scope < 0) throw new ForbiddenException();
        return await _users.ListAsync(request, scope, actor.UserId, actor.AllowedBranchIds,
            actor.IsAdmin, actor.IsSuperAdmin, ct);
    }

    public async Task<UserDetail> GetAsync(ActorContext actor, Guid id, CancellationToken ct = default)
    {
        await _permissions.RequireAsync(actor, ScreenCodes.Users, ActionCodes.View, ct: ct);
        var detail = await _users.GetDetailAsync(id, ct) ?? throw new NotFoundAppException();
        EnsureTargetVisible(actor, detail.User);
        if (!actor.IsAdmin && !detail.BranchIds.Any(actor.CanAccessBranch) && id != actor.UserId)
            throw new ForbiddenException();
        return detail;
    }

    internal static void Normalize(UserInput input)
    {
        input.Username = (input.Username ?? "").Trim();
        input.Name = string.IsNullOrWhiteSpace(input.Name) ? null : input.Name.Trim();
        input.Language = input.Language == "en" ? "en" : "ar";
        if (input.DefaultBranchId.HasValue && !input.BranchIds.Contains(input.DefaultBranchId.Value))
            input.DefaultBranchId = input.BranchIds.FirstOrDefault();
    }

    internal static void EnsureTargetVisible(ActorContext actor, User target)
    {
        if (target.IsSuperAdmin && !actor.IsSuperAdmin)
            throw new ForbiddenException();
    }
}
