namespace Ubp.Core.Models;

public sealed class Currency
{
    public Guid Id { get; set; }
    public string CurrencyCode { get; set; } = "";
    public string NameAr { get; set; } = "";
    public string? NameEn { get; set; }
    public string? Symbol { get; set; }
    public short Decimals { get; set; } = 2;
    public bool IsBase { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public int Version { get; set; } = 1;
}

public sealed class CurrencyInput
{
    public string CurrencyCode { get; set; } = "";
    public string NameAr { get; set; } = "";
    public string? NameEn { get; set; }
    public string? Symbol { get; set; }
    public short Decimals { get; set; } = 2;
    public bool IsBase { get; set; }
}
