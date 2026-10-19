namespace Ubp.Core.Models;

public sealed class CustomFieldDefinition
{
    public Guid Id { get; set; }
    public string EntityName { get; set; } = "";
    public string FieldKey { get; set; } = "";
    public string LabelAr { get; set; } = "";
    public string? LabelEn { get; set; }
    public string DataType { get; set; } = "text";
    public List<string> Options { get; set; } = new();
    public bool IsRequired { get; set; }
    public bool IsActive { get; set; } = true;
    public int SortOrder { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public int Version { get; set; } = 1;
}

public sealed class CustomFieldInput
{
    public string EntityName { get; set; } = "";
    public string FieldKey { get; set; } = "";
    public string LabelAr { get; set; } = "";
    public string? LabelEn { get; set; }
    public string DataType { get; set; } = "text";
    public List<string> Options { get; set; } = new();
    public bool IsRequired { get; set; }
    public bool IsActive { get; set; } = true;
    public int SortOrder { get; set; }
}
