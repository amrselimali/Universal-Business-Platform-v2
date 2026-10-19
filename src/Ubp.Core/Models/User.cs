namespace Ubp.Core.Models;

public sealed class User
{
    public Guid Id { get; set; }
    public string? Name { get; set; }
    public string Username { get; set; } = "";
    public string PasswordHash { get; set; } = "";
    public bool IsActive { get; set; } = true;
    public string Language { get; set; } = "ar";
    public bool IsAdmin { get; set; }
    public bool IsSuperAdmin { get; set; }
    public short FailedAttempts { get; set; }
    public DateTimeOffset? LockedUntil { get; set; }
    public DateTimeOffset? LastLoginAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public int Version { get; set; } = 1;

    public bool IsLocked => LockedUntil.HasValue && LockedUntil.Value > DateTimeOffset.UtcNow;
}

public sealed class UserListItem
{
    public Guid Id { get; set; }
    public string? Name { get; set; }
    public string Username { get; set; } = "";
    public bool IsActive { get; set; }
    public bool IsAdmin { get; set; }
    public bool IsSuperAdmin { get; set; }
    public bool IsLocked { get; set; }
    public DateTimeOffset? LastLoginAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public int Version { get; set; }
    public int BranchCount { get; set; }
    public int WarehouseCount { get; set; }
}

public sealed class UserDetail
{
    public User User { get; set; } = new();
    public List<Guid> BranchIds { get; set; } = new();
    public Guid? DefaultBranchId { get; set; }
    public List<Guid> WarehouseIds { get; set; } = new();
    public List<Guid> RoleIds { get; set; } = new();
}

public sealed class UserInput
{
    public string? Name { get; set; }
    public string Username { get; set; } = "";
    public string? Password { get; set; }
    public bool IsActive { get; set; } = true;
    public string Language { get; set; } = "ar";
    public bool IsAdmin { get; set; }
    public bool IsSuperAdmin { get; set; }
    public List<Guid> BranchIds { get; set; } = new();
    public Guid? DefaultBranchId { get; set; }
    public List<Guid> WarehouseIds { get; set; } = new();
    public List<Guid> RoleIds { get; set; } = new();
}
