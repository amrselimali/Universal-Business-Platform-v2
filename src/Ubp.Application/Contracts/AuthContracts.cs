using Ubp.Core.Models;

namespace Ubp.Application.Contracts;

public interface IAccessTokenIssuer
{
    string Create(User user, Guid sessionId, Guid? branchId);
}

public sealed class LoginResult
{
    public bool Success { get; init; }
    public string? ErrorKey { get; init; }
    public User? User { get; init; }
    public Guid? SessionId { get; init; }
    public Guid? BranchId { get; init; }
    public string? AccessToken { get; init; }
    public string? RefreshToken { get; init; }

    public static LoginResult Fail(string key) => new() { Success = false, ErrorKey = key };
}

public sealed class SessionUser
{
    public required Guid SessionId { get; init; }
    public required User User { get; init; }
    public Guid? BranchId { get; init; }
    public required IReadOnlyList<Guid> BranchIds { get; init; }
    public required IReadOnlyList<Guid> WarehouseIds { get; init; }
    public string UserLanguage { get; init; } = "ar";
}
