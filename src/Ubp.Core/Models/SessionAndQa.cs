namespace Ubp.Core.Models;

public sealed class UserSession
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public Guid? BranchId { get; set; }
    public Guid? RefreshTokenId { get; set; }
    public string? IpAddress { get; set; }
    public DateTimeOffset LastSeenAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public bool IsActive => RevokedAt is null && ExpiresAt > DateTimeOffset.UtcNow;
}

public sealed class QaTestDefinition
{
    public string Code { get; set; } = "";
    public string NameAr { get; set; } = "";
    public string NameEn { get; set; } = "";
    public string Category { get; set; } = "";
    public string Criticality { get; set; } = "medium";
}

public sealed class QaTestResult
{
    public string TestCode { get; set; } = "";
    public string Status { get; set; } = "Passed";
    public int DurationMs { get; set; }
    public string? Message { get; set; }
    public string? TraceId { get; set; }
}

public sealed class QaRunSummary
{
    public Guid Id { get; set; }
    public string Environment { get; set; } = "";
    public string? CodeRevision { get; set; }
    public string Status { get; set; } = "";
    public int TotalCount { get; set; }
    public int PassedCount { get; set; }
    public int FailedCount { get; set; }
    public string? TraceId { get; set; }
    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset? FinishedAt { get; set; }
}
