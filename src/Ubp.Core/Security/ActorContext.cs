namespace Ubp.Core.Security;

public sealed class ActorContext
{
    public Guid UserId { get; init; }
    public string Username { get; init; } = "";
    public string? Name { get; init; }
    public bool IsAdmin { get; init; }
    public bool IsSuperAdmin { get; init; }
    public string Language { get; init; } = "ar";
    public Guid? CurrentBranchId { get; init; }
    public IReadOnlyCollection<Guid> AllowedBranchIds { get; init; } = Array.Empty<Guid>();
    public string? IpAddress { get; init; }
    public string TraceId { get; init; } = "";

    public bool CanAccessBranch(Guid branchId) => IsAdmin || AllowedBranchIds.Contains(branchId);
    public bool HasAnyBranchAccess(IEnumerable<Guid> branchIds) =>
        IsAdmin || branchIds.Any(CanAccessBranch);
    public string DisplayName => string.IsNullOrWhiteSpace(Name) ? Username : Name;
}
