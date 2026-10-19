namespace Ubp.Core.Models;

public sealed class Warehouse
{
    public Guid Id { get; set; }
    public string Code { get; set; } = "";
    public string NameAr { get; set; } = "";
    public string? NameEn { get; set; }
    public bool IsActive { get; set; } = true;
    public Dictionary<string, object?> CustomFields { get; set; } = new();
    public List<Guid> BranchIds { get; set; } = new();
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public int Version { get; set; } = 1;
}

public sealed class WarehouseInput
{
    public string Code { get; set; } = "";
    public string NameAr { get; set; } = "";
    public string? NameEn { get; set; }
    public bool IsActive { get; set; } = true;
    public Dictionary<string, object?> CustomFields { get; set; } = new();
    public List<Guid> BranchIds { get; set; } = new();
}
