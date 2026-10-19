using Microsoft.Extensions.Logging;
using Ubp.Application.Contracts;
using Ubp.Core.Models;
using Ubp.Infrastructure.Repositories;

namespace Ubp.Application.Services;

public sealed partial class AuthService
{
    private async Task<LoginResult> IssueAsync(User user, Guid? branchId, string? ip,
        string traceId, CancellationToken ct)
    {
        var settings = await _settings.GetAsync(ct);
        var refreshRaw = NewRawToken();
        var refreshId = await _refreshTokens.CreateAsync(user.Id, HashToken(refreshRaw),
            DateTimeOffset.UtcNow.AddDays(30), ip, ct);

        var expires = DateTimeOffset.UtcNow.AddMinutes(settings.SessionTimeoutMinutes);
        var sessionId = await _sessions.CreateSessionAsync(user.Id, branchId, refreshId, ip, expires, ct);
        var access = _tokens.Create(user, sessionId, branchId);
        _logger.LogInformation("Login success for {Username}, session {SessionId}, trace {TraceId}",
            user.Username, sessionId, traceId);
        return new LoginResult
        {
            Success = true, User = user, SessionId = sessionId, BranchId = branchId,
            AccessToken = access, RefreshToken = refreshRaw
        };
    }

    public async Task<SessionUser?> ValidateSessionAsync(Guid sessionId, string? ip,
        string traceId, CancellationToken ct = default)
    {
        var session = await _sessions.GetSessionAsync(sessionId, ct);
        if (session is null || !session.IsActive) return null;

        var user = await _users.GetByIdAsync(session.UserId, ct);
        if (user is null || !user.IsActive)
        {
            await _sessions.RevokeSessionAsync(sessionId, ct);
            return null;
        }

        var settings = await _settings.GetAsync(ct);
        var branchIds = await _users.GetBranchIdsAsync(user.Id, ct);
        var branchId = session.BranchId;
        if (branchId.HasValue && !user.IsAdmin && !branchIds.Contains(branchId.Value))
            branchId = await _users.GetDefaultBranchIdAsync(user.Id, ct);
        if (!branchId.HasValue && !user.IsAdmin && branchIds.Count > 0)
            branchId = await _users.GetDefaultBranchIdAsync(user.Id, ct);

        var timeout = TimeSpan.FromMinutes(settings.SessionTimeoutMinutes);
        var expires = session.ExpiresAt;
        if (DateTimeOffset.UtcNow > expires)
        {
            await _sessions.RevokeSessionAsync(sessionId, ct);
            return null;
        }

        if (expires - DateTimeOffset.UtcNow < TimeSpan.FromTicks(timeout.Ticks / 2))
        {
            expires = DateTimeOffset.UtcNow.Add(timeout);
            await _sessions.TouchSessionAsync(sessionId, expires, ct);
        }
        if (branchId != session.BranchId)
            await _sessions.SetSessionBranchAsync(sessionId, branchId, expires, ct);

        return new SessionUser
        {
            SessionId = sessionId, User = user, BranchId = branchId,
            BranchIds = branchIds,
            WarehouseIds = await _users.GetWarehouseIdsAsync(user.Id, ct),
            UserLanguage = user.Language
        };
    }

    public async Task<bool> SwitchBranchAsync(Guid sessionId, Guid userId, Guid? branchId,
        CancellationToken ct = default)
    {
        var user = await _users.GetByIdAsync(userId, ct);
        if (user is null) return false;

        if (branchId.HasValue)
        {
            if (!user.IsAdmin)
            {
                var allowed = await _users.GetBranchIdsAsync(userId, ct);
                if (!allowed.Contains(branchId.Value)) return false;
            }
            else if (await _branches.GetAsync(branchId.Value, ct) is null)
                return false;
        }
        else if (!user.IsAdmin)
            return false;

        var settings = await _settings.GetAsync(ct);
        await _sessions.SetSessionBranchAsync(sessionId, branchId,
            DateTimeOffset.UtcNow.AddMinutes(settings.SessionTimeoutMinutes), ct);
        return true;
    }

    public async Task LogoutAsync(Guid sessionId, Guid userId, Guid? branchId, string? ip,
        string traceId, CancellationToken ct = default)
    {
        await _sessions.RevokeSessionAsync(sessionId, ct);
        await _refreshTokens.RevokeAllForUserAsync(userId, ct);
        await _sessions.RevokeAllSessionsAsync(userId, ct);
        await AuditLoginAsync(userId, branchId, "LOGOUT", ip, traceId, ct);
    }

    public async Task<LoginResult> RefreshAsync(string rawRefreshToken, string? ip,
        string traceId, CancellationToken ct = default)
    {
        var existing = await _refreshTokens.GetActiveAsync(HashToken(rawRefreshToken), ct);
        if (existing is null) return LoginResult.Fail("auth.invalid_refresh");
        var user = await _users.GetByIdAsync(existing.Value.UserId, ct);
        if (user is null || !user.IsActive) return LoginResult.Fail("auth.invalid_refresh");

        var newRaw = NewRawToken();
        var newId = await _refreshTokens.CreateAsync(user.Id, HashToken(newRaw),
            DateTimeOffset.UtcNow.AddDays(30), ip, ct);
        await _refreshTokens.RevokeAsync(existing.Value.Id, newId, ct);

        var branchId = await _users.GetDefaultBranchIdAsync(user.Id, ct);
        var issued = await IssueAsync(user, branchId, ip, traceId, ct);
        return new LoginResult
        {
            Success = true, User = user, SessionId = issued.SessionId, BranchId = branchId,
            AccessToken = issued.AccessToken, RefreshToken = newRaw
        };
    }
}
