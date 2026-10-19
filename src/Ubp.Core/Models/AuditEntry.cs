namespace Ubp.Core.Models;

public sealed class AuditEntry
{
    public long Id { get; set; }
    public Guid? UserId { get; set; }
    public string? Username { get; set; }
    public Guid? BranchId { get; set; }
    public string? BranchName { get; set; }
    public string TableName { get; set; } = "";
    public string RecordId { get; set; } = "";
    public string Action { get; set; } = "";
    public string? OldData { get; set; }
    public string? NewData { get; set; }
    public string? IpAddress { get; set; }
    public string? TraceId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class AuditFilter
{
    public DateTimeOffset? From { get; set; }
    public DateTimeOffset? To { get; set; }
    public Guid? UserId { get; set; }
    public Guid? BranchId { get; set; }
    public string? TableName { get; set; }
    public string? Action { get; set; }
    public long? BeforeId { get; set; }
}

public sealed class AuditWrite
{
    public Guid? UserId { get; set; }
    public Guid? BranchId { get; set; }
    public string TableName { get; set; } = "";
    public string RecordId { get; set; } = "";
    public string Action { get; set; } = "";
    public object? OldData { get; set; }
    public object? NewData { get; set; }
    public string? IpAddress { get; set; }
    public string? TraceId { get; set; }
}
