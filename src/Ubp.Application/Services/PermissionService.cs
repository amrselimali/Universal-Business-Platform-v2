using Microsoft.Extensions.Caching.Memory;
using Ubp.Core.Models;
using Ubp.Core.Results;
using Ubp.Core.Security;
using Ubp.Infrastructure.Repositories;

namespace Ubp.Application.Services;

public sealed class PermissionService
{
    private static int _version;
    public static void InvalidateAll() => Interlocked.Increment(ref _version);
    private static string CacheKey(Guid userId) => $"perm:{userId}:{Volatile.Read(ref _version)}";

    private readonly IPermissionRepository _permissions;
    private readonly IMemoryCache _cache;

    public PermissionService(IPermissionRepository permissions, IMemoryCache cache)
    {
        _permissions = permissions;
        _cache = cache;
    }

    public async Task<IReadOnlyDictionary<string, EffectivePermission>> GetSnapshotAsync(
        Guid userId, bool isAdmin, CancellationToken ct = default)
    {
        var key = CacheKey(userId);
        if (_cache.TryGetValue(key, out IReadOnlyDictionary<string, EffectivePermission>? cached) && cached is not null)
            return cached;

        var grants = isAdmin
            ? (IReadOnlyList<PermissionEntry>)Array.Empty<PermissionEntry>()
            : (await _permissions.GetRoleGrantsForUserAsync(userId, ct)).ToList();
        var overrides = isAdmin
            ? (IReadOnlyList<PermissionEntry>)Array.Empty<PermissionEntry>()
            : (await _permissions.GetUserOverridesAsync(userId, ct)).ToList();

        var map = new Dictionary<string, EffectivePermission>(StringComparer.OrdinalIgnoreCase);
        foreach (var screen in AppRegistry.Screens)
        {
            foreach (var action in screen.Actions)
            {
                var effective = PermissionEvaluator.Evaluate(grants, overrides, isAdmin, screen.Code, action.Code);
                map[Key(screen.Code, action.Code)] = effective;
            }
        }

        _cache.Set(key, (IReadOnlyDictionary<string, EffectivePermission>)map, TimeSpan.FromMinutes(5));
        return map;
    }

    public async Task<EffectivePermission> GetAsync(ActorContext actor, string screenCode, string actionCode,
        CancellationToken ct = default)
    {
        if (!AppRegistry.IsRegistered(screenCode, actionCode))
            return new EffectivePermission { ScreenCode = screenCode, ActionCode = actionCode };

        var snapshot = await GetSnapshotAsync(actor.UserId, actor.IsAdmin, ct);
        return snapshot.TryGetValue(Key(screenCode, actionCode), out var permission)
            ? permission
            : new EffectivePermission { ScreenCode = screenCode, ActionCode = actionCode };
    }

    public async Task<bool> HasAsync(ActorContext actor, string screenCode, string actionCode,
        CancellationToken ct = default) =>
        (await GetAsync(actor, screenCode, actionCode, ct)).Allowed;

    public async Task<short> GetScopeAsync(ActorContext actor, string screenCode, string actionCode,
        CancellationToken ct = default)
    {
        var permission = await GetAsync(actor, screenCode, actionCode, ct);
        return permission.Allowed ? permission.Scope : (short)-1;
    }

    public async Task RequireAsync(ActorContext actor, string screenCode, string actionCode,
        short? minimumScope = null, CancellationToken ct = default)
    {
        var permission = await GetAsync(actor, screenCode, actionCode, ct);
        if (!permission.Allowed)
            throw new ForbiddenException();

        if (minimumScope.HasValue && permission.Scope < minimumScope.Value)
            throw new ForbiddenException();
    }

    public async Task<IReadOnlyList<EffectivePermission>> GetMatrixAsync(ActorContext actor,
        Guid targetUserId, bool targetIsAdmin, CancellationToken ct = default)
    {
        var snapshot = await GetSnapshotAsync(targetUserId, targetIsAdmin, ct);
        return AppRegistry.Screens
            .SelectMany(s => s.Actions.Select(a =>
                snapshot.TryGetValue(Key(s.Code, a.Code), out var p)
                    ? p
                    : new EffectivePermission { ScreenCode = s.Code, ActionCode = a.Code }))
            .OrderBy(p => p.ScreenCode).ThenBy(p => p.ActionCode)
            .ToList();
    }

    public async Task<IReadOnlyList<string>> GetVisibleScreenCodesAsync(ActorContext actor,
        CancellationToken ct = default)
    {
        var snapshot = await GetSnapshotAsync(actor.UserId, actor.IsAdmin, ct);
        return AppRegistry.Screens
            .Where(s => snapshot.TryGetValue(Key(s.Code, ActionCodes.View), out var p) && p.Allowed)
            .Select(s => s.Code)
            .ToList();
    }

    private static string Key(string screen, string action) => $"{screen}/{action}";
}
