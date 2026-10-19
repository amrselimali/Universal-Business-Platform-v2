using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using Ubp.Application.Contracts;
using Ubp.Core.Models;
using Ubp.Core.Results;
using Ubp.Infrastructure.Repositories;
using Ubp.Infrastructure.Security;

namespace Ubp.Application.Services;

public sealed partial class AuthService
{
    private readonly IUserRepository _users;
    private readonly ISessionRepository _sessions;
    private readonly IRefreshTokenRepository _refreshTokens;
    private readonly IBranchRepository _branches;
    private readonly ISettingsService _settings;
    private readonly IAuditRepository _audit;
    private readonly IAccessTokenIssuer _tokens;
    private readonly PasswordHasher _hasher;
    private readonly ILogger<AuthService> _logger;

    public AuthService(IUserRepository users, ISessionRepository sessions,
        IRefreshTokenRepository refreshTokens, IBranchRepository branches,
        ISettingsService settings, IAuditRepository audit, IAccessTokenIssuer tokens,
        PasswordHasher hasher, ILogger<AuthService> logger)
    {
        _users = users; _sessions = sessions; _refreshTokens = refreshTokens; _branches = branches;
        _settings = settings; _audit = audit; _tokens = tokens; _hasher = hasher; _logger = logger;
    }

    public async Task<LoginResult> LoginAsync(string username, string password, string? ip,
        string traceId, CancellationToken ct = default)
    {
        var user = await _users.GetAuthByUsernameAsync(username.Trim(), ct);
        if (user is null)
        {
            _hasher.FakeVerify(password);
            await AuditLoginAsync(null, null, "LOGIN_FAILED", ip, traceId, ct);
            return LoginResult.Fail("auth.invalid_credentials");
        }

        if (!user.IsActive || user.IsLocked)
        {
            _hasher.FakeVerify(password);
            await AuditLoginAsync(user.Id, null, "LOGIN_FAILED", ip, traceId, ct);
            return LoginResult.Fail("auth.invalid_credentials");
        }

        if (!_hasher.Verify(password, user.PasswordHash))
        {
            var settings = await _settings.GetAsync(ct);
            await _users.RegisterFailedLoginAsync(user.Id, settings.MaxFailedAttempts,
                settings.LockoutMinutes, ct);
            await AuditLoginAsync(user.Id, null, "LOGIN_FAILED", ip, traceId, ct);
            return LoginResult.Fail("auth.invalid_credentials");
        }

        var branchIds = await _users.GetBranchIdsAsync(user.Id, ct);
        if (!user.IsAdmin && branchIds.Count == 0)
        {
            _logger.LogWarning("Login rejected: user {UserId} has no branches", user.Id);
            await AuditLoginAsync(user.Id, null, "LOGIN_FAILED", ip, traceId, ct);
            return LoginResult.Fail("auth.no_branch");
        }

        var branchId = user.IsAdmin ? null : await _users.GetDefaultBranchIdAsync(user.Id, ct);
        await _users.RegisterSuccessfulLoginAsync(user.Id, ct);
        var issued = await IssueAsync(user, branchId, ip, traceId, ct);
        await AuditLoginAsync(user.Id, branchId, "LOGIN", ip, traceId, ct);
        return issued;
    }

    private Task AuditLoginAsync(Guid? userId, Guid? branchId, string action, string? ip,
        string traceId, CancellationToken ct) =>
        _audit.WriteAsync(new AuditWrite
        {
            UserId = userId, BranchId = branchId, TableName = "users",
            RecordId = userId?.ToString() ?? "anonymous", Action = action,
            IpAddress = ip, TraceId = traceId
        }, ct);
}
