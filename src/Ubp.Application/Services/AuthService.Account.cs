using System.Security.Cryptography;
using System.Text;
using Ubp.Core.Models;
using Ubp.Core.Results;
using Ubp.Core.Security;

namespace Ubp.Application.Services;

public sealed partial class AuthService
{
    public async Task<OperationResult> ChangePasswordAsync(ActorContext actor, string currentPassword,
        string newPassword, CancellationToken ct = default)
    {
        var user = await _users.GetByIdAsync(actor.UserId, ct);
        if (user is null) throw new NotFoundAppException();
        if (!_hasher.Verify(currentPassword, user.PasswordHash))
            throw new ValidationAppException("validation.current_password_wrong");

        var error = await _settings.ValidatePasswordAsync(newPassword, user.Username, ct);
        if (error is not null) throw new ValidationAppException(error);

        await _users.ResetPasswordAsync(user.Id, _hasher.Hash(newPassword), ct);
        await _sessions.RevokeAllSessionsAsync(user.Id, ct);
        await _refreshTokens.RevokeAllForUserAsync(user.Id, ct);
        await _audit.WriteAsync(new AuditWrite
        {
            UserId = actor.UserId, BranchId = actor.CurrentBranchId, TableName = "users",
            RecordId = user.Id.ToString(), Action = "PASSWORD_RESET",
            IpAddress = actor.IpAddress, TraceId = actor.TraceId
        }, ct);
        return OperationResult.Ok("auth.password_changed");
    }

    public Task UpdateLanguageAsync(Guid userId, string language, CancellationToken ct = default) =>
        _users.UpdateLanguageAsync(userId, language, ct);

    private static string NewRawToken() =>
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(48))
            .Replace('+', '-').Replace('/', '_').TrimEnd('=');

    private static string HashToken(string token) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token))).ToLowerInvariant();
}
