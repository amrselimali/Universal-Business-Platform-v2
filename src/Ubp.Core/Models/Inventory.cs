namespace Ubp.Core.Models;

public sealed class Unit
{
    public Guid Id { get; set; }
    public string Code { get; set; } = "";
    public string NameAr { get; set; } = "";
    public string? NameEn { get; set; }
    public short Decimals { get; set; } = 2;
    public bool IsActive { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public int Version { get; set; } = 1;
}

public sealed class UnitInput
{
    public string Code { get; set; } = "";
    public string NameAr { get; set; } = "";
    public string? NameEn { get; set; }
    public short Decimals { get; set; } = 2;
    public bool IsActive { get; set; } = true;
}

public sealed class ItemCategory
{
    public Guid Id { get; set; }
    public string Code { get; set; } = "";
    public string NameAr { get; set; } = "";
    public string? NameEn { get; set; }
    public Guid? ParentId { get; set; }
    public string? ParentName { get; set; }
    public int ItemCount { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public int Version { get; set; } = 1;
}

public sealed class ItemCategoryInput
{
    public string Code { get; set; } = "";
    public string NameAr { get; set; } = "";
    public string? NameEn { get; set; }
    public Guid? ParentId { get; set; }
    public bool IsActive { get; set; } = true;
}

public sealed class Item
{
    public Guid Id { get; set; }
    public string Code { get; set; } = "";
    public string NameAr { get; set; } = "";
    public string? NameEn { get; set; }
    public Guid? CategoryId { get; set; }
    public string? CategoryName { get; set; }
    public Guid BaseUnitId { get; set; }
    public string BaseUnitName { get; set; } = "";
    public string ItemType { get; set; } = "GOODS";
    public Guid? TaxId { get; set; }
    public string? TaxName { get; set; }
    public bool PriceIncludesTax { get; set; }
    public bool TrackExpiry { get; set; }
    public bool IsConsumable { get; set; }
    public decimal? DefaultPrice { get; set; }
    public string? Notes { get; set; }
    public bool IsActive { get; set; } = true;
    public Dictionary<string, object?> CustomFields { get; set; } = new();
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public int Version { get; set; } = 1;
}

public sealed class ItemInput
{
    public string Code { get; set; } = "";
    public string NameAr { get; set; } = "";
    public string? NameEn { get; set; }
    public Guid? CategoryId { get; set; }
    public Guid BaseUnitId { get; set; }
    public string ItemType { get; set; } = "GOODS";
    public Guid? TaxId { get; set; }
    public bool PriceIncludesTax { get; set; }
    public bool TrackExpiry { get; set; }
    public bool IsConsumable { get; set; }
    public decimal? DefaultPrice { get; set; }
    public string? Notes { get; set; }
    public bool IsActive { get; set; } = true;
    public Dictionary<string, object?> CustomFields { get; set; } = new();
    public List<ItemUnitInput> Units { get; set; } = new();
    public List<string> Barcodes { get; set; } = new();
}

public sealed class ItemUnit
{
    public Guid Id { get; set; }
    public Guid ItemId { get; set; }
    public Guid UnitId { get; set; }
    public string UnitName { get; set; } = "";
    public string UnitCode { get; set; } = "";
    public decimal ConversionFactor { get; set; } = 1;
    public decimal? SalePrice { get; set; }
    public decimal? PurchasePrice { get; set; }
    public bool IsActive { get; set; } = true;
    public int Version { get; set; } = 1;
}

public sealed class ItemUnitInput
{
    public Guid UnitId { get; set; }
    public decimal ConversionFactor { get; set; } = 1;
    public decimal? SalePrice { get; set; }
    public decimal? PurchasePrice { get; set; }
    public bool IsActive { get; set; } = true;
}

public sealed class ItemBarcode
{
    public Guid Id { get; set; }
    public Guid ItemId { get; set; }
    public Guid? UnitId { get; set; }
    public string? UnitName { get; set; }
    public string Barcode { get; set; } = "";
    public bool IsActive { get; set; } = true;
}

public sealed class ItemDetail
{
    public Item Item { get; set; } = new();
    public IReadOnlyList<ItemUnit> Units { get; set; } = Array.Empty<ItemUnit>();
    public IReadOnlyList<ItemBarcode> Barcodes { get; set; } = Array.Empty<ItemBarcode>();
}

public sealed class ItemOption
{
    public Guid Id { get; set; }
    public string Code { get; set; } = "";
    public string NameAr { get; set; } = "";
    public string? NameEn { get; set; }
    public Guid BaseUnitId { get; set; }
    public string BaseUnitName { get; set; } = "";
    public decimal OnHand { get; set; }
    public string ItemType { get; set; } = "GOODS";
    public Guid? TaxId { get; set; }
    public decimal TaxRate { get; set; }
    public decimal DefaultPrice { get; set; }
    public bool PriceIncludesTax { get; set; }
    public bool IsConsumable { get; set; }
}
