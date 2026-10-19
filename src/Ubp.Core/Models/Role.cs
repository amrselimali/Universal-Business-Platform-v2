namespace Ubp.Core.Models;

public sealed class Role
{
    public Guid Id { get; set; }
    public string Code { get; set; } = "";
    public string NameAr { get; set; } = "";
    public string? NameEn { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public int Version { get; set; } = 1;
}

public sealed class RoleInput
{
    public string Code { get; set; } = "";
    public string NameAr { get; set; } = "";
    public string? NameEn { get; set; }
    public bool IsActive { get; set; } = true;
    public List<PermissionEntry> Permissions { get; set; } = new();
}

public sealed class PermissionEntry
{
    public string ScreenCode { get; set; } = "";
    public string ActionCode { get; set; } = "";
    public bool IsAllowed { get; set; }
    public short Scope { get; set; } = 1;
}

public sealed class EffectivePermission
{
    public string ScreenCode { get; set; } = "";
    public string ActionCode { get; set; } = "";
    public bool Allowed { get; set; }
    public short Scope { get; set; }
    public string Source { get; set; } = "none";
    public bool HasOverride { get; set; }
    public bool RoleAllowed { get; set; }
    public short RoleScope { get; set; }
}
