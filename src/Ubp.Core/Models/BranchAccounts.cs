namespace Ubp.Core.Models;

/// <summary>حساب افتراضي لفرع (الترحيل 0048): المفتاح مثل cash / inventory / revenue / customer_advances.</summary>
public sealed class BranchAccountDefault
{
    public Guid BranchId { get; set; }
    public string Key { get; set; } = "";
    public Guid AccountId { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public sealed record BranchAccountDefaultInput(Guid BranchId, string Key, Guid AccountId);
