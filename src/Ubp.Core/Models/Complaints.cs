namespace Ubp.Core.Models;

public static class ComplaintStatuses
{
    public const string Open = "OPEN";
    public const string Closed = "CLOSED";
}

public sealed class ComplaintInput
{
    public Guid BranchId { get; set; }
    public Guid? CustomerId { get; set; }
    /// <summary>الموظف المتسبب في الشكوى.</summary>
    public Guid? EmployeeId { get; set; }
    /// <summary>الخدمة/الصنف محل الشكوى (اختياري).</summary>
    public Guid? ItemId { get; set; }
    public DateTime DocDate { get; set; } = DateTime.Today;
    public string Description { get; set; } = "";
}

public sealed class Complaint
{
    public Guid Id { get; set; }
    public Guid BranchId { get; set; }
    public string? BranchName { get; set; }
    public Guid? CustomerId { get; set; }
    public string? CustomerName { get; set; }
    public Guid? EmployeeId { get; set; }
    public string? EmployeeName { get; set; }
    /// <summary>الخدمة/الصنف محل الشكوى (اختياري).</summary>
    public Guid? ItemId { get; set; }
    public string? ItemName { get; set; }
    public DateTime DocDate { get; set; }
    public string Description { get; set; } = "";
    public string Status { get; set; } = ComplaintStatuses.Open;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? ClosedAt { get; set; }

    public bool IsOpen => Status == ComplaintStatuses.Open;
}
