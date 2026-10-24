namespace Ubp.Core.Models;

public static class WarehouseTypes
{
    public const string Main = "MAIN";
    public const string Consumables = "CONSUMABLES";
    public static readonly string[] All = { Main, Consumables };
}

public static class ConsumptionModes
{
    public const short Fixed = 0;
    public const short Editable = 1;
    public const short Manual = 2;
}

public static class CollectionPolicies
{
    public const short NotRequired = 0;
    public const short Optional = 1;
    public const short Required = 2;
}

public static class InvoiceGroupingModes
{
    public const short Immediate = 0;
    public const short OnBookingClose = 1;
    public const short Manual = 2;
}

public sealed class Employee
{
    public Guid Id { get; set; }
    public string Code { get; set; } = "";
    public string NameAr { get; set; } = "";
    public string? NameEn { get; set; }
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public Guid? UserId { get; set; }
    public bool IsBookingReceiver { get; set; }
    public bool IsSeller { get; set; }
    public bool IsServiceExecutor { get; set; }
    public DateTime? HireDate { get; set; }
    public string? Notes { get; set; }
    public bool IsActive { get; set; } = true;
    public int Version { get; set; } = 1;
    public int BookingsCount { get; set; }

    public string DisplayName(bool english) =>
        english && !string.IsNullOrWhiteSpace(NameEn) ? NameEn! : NameAr;
}

public sealed class EmployeeInput
{
    public string Code { get; set; } = "";
    public string NameAr { get; set; } = "";
    public string? NameEn { get; set; }
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public Guid? UserId { get; set; }
    public bool IsBookingReceiver { get; set; }
    public bool IsSeller { get; set; }
    public bool IsServiceExecutor { get; set; }
    public DateTime? HireDate { get; set; }
    public string? Notes { get; set; }
    public bool IsActive { get; set; } = true;
}

public sealed class Tax
{
    public Guid Id { get; set; }
    public string Code { get; set; } = "";
    public string NameAr { get; set; } = "";
    public string? NameEn { get; set; }
    public decimal Rate { get; set; }
    public bool IsInclusiveAllowed { get; set; } = true;
    public Guid? OutputAccountId { get; set; }
    public Guid? InputAccountId { get; set; }
    public string? Notes { get; set; }
    public bool IsActive { get; set; } = true;
    public int Version { get; set; } = 1;
    public int ItemsCount { get; set; }

    public string DisplayName(bool english) =>
        english && !string.IsNullOrWhiteSpace(NameEn) ? NameEn! : NameAr;
}

public sealed class TaxInput
{
    public string Code { get; set; } = "";
    public string NameAr { get; set; } = "";
    public string? NameEn { get; set; }
    public decimal Rate { get; set; }
    public bool IsInclusiveAllowed { get; set; } = true;
    public Guid? OutputAccountId { get; set; }
    public Guid? InputAccountId { get; set; }
    public string? Notes { get; set; }
    public bool IsActive { get; set; } = true;
}

public sealed class PaymentTerm
{
    public Guid Id { get; set; }
    public string Code { get; set; } = "";
    public string NameAr { get; set; } = "";
    public string? NameEn { get; set; }
    public short NetDays { get; set; }
    public decimal DiscountPercent { get; set; }
    public short DiscountDays { get; set; }
    public string? Notes { get; set; }
    public bool IsActive { get; set; } = true;
    public int Version { get; set; } = 1;

    public string DisplayName(bool english) =>
        english && !string.IsNullOrWhiteSpace(NameEn) ? NameEn! : NameAr;
}

public sealed class PaymentTermInput
{
    public string Code { get; set; } = "";
    public string NameAr { get; set; } = "";
    public string? NameEn { get; set; }
    public short NetDays { get; set; }
    public decimal DiscountPercent { get; set; }
    public short DiscountDays { get; set; }
    public string? Notes { get; set; }
    public bool IsActive { get; set; } = true;
}

public sealed class PriceList
{
    public Guid Id { get; set; }
    public string Code { get; set; } = "";
    public string NameAr { get; set; } = "";
    public string? NameEn { get; set; }
    public string Currency { get; set; } = "EGP";
    public bool IsDefault { get; set; }
    public DateTime? ValidFrom { get; set; }
    public DateTime? ValidTo { get; set; }
    public string? Notes { get; set; }
    public bool IsActive { get; set; } = true;
    public int Version { get; set; } = 1;
    public int ItemsCount { get; set; }

    public string DisplayName(bool english) =>
        english && !string.IsNullOrWhiteSpace(NameEn) ? NameEn! : NameAr;
}

public sealed class PriceListInput
{
    public string Code { get; set; } = "";
    public string NameAr { get; set; } = "";
    public string? NameEn { get; set; }
    public bool IsDefault { get; set; }
    public DateTime? ValidFrom { get; set; }
    public DateTime? ValidTo { get; set; }
    public string? Notes { get; set; }
    public bool IsActive { get; set; } = true;
}

public sealed class PriceListItem
{
    public Guid Id { get; set; }
    public Guid PriceListId { get; set; }
    public Guid ItemId { get; set; }
    public string? ItemCode { get; set; }
    public string? ItemName { get; set; }
    public Guid? UnitId { get; set; }
    public string? UnitName { get; set; }
    public decimal Price { get; set; }
    public decimal MinQty { get; set; }
}

public sealed class PriceListItemInput
{
    public Guid ItemId { get; set; }
    public Guid? UnitId { get; set; }
    public decimal Price { get; set; }
    public decimal MinQty { get; set; }
}

public sealed class ServiceRecipe
{
    public Guid Id { get; set; }
    public Guid ItemId { get; set; }
    public string? ItemCode { get; set; }
    public string? ItemName { get; set; }
    public string? Notes { get; set; }
    public bool IsActive { get; set; } = true;
    public int Version { get; set; } = 1;
    public IReadOnlyList<ServiceRecipeItem> Items { get; set; } = Array.Empty<ServiceRecipeItem>();
}

public sealed class ServiceRecipeItem
{
    public Guid Id { get; set; }
    public Guid RecipeId { get; set; }
    public int LineNo { get; set; }
    public Guid ComponentItemId { get; set; }
    public string? ComponentCode { get; set; }
    public string? ComponentName { get; set; }
    public Guid UnitId { get; set; }
    public string? UnitName { get; set; }
    public decimal Quantity { get; set; }
    public short ConsumptionMode { get; set; }
    public bool IsRequired { get; set; } = true;
    public string? Notes { get; set; }
    public IReadOnlyList<Guid> SubstituteItemIds { get; set; } = Array.Empty<Guid>();
}

public sealed class ServiceRecipeItemInput
{
    public Guid ComponentItemId { get; set; }
    public Guid UnitId { get; set; }
    public decimal Quantity { get; set; }
    public short ConsumptionMode { get; set; }
    public bool IsRequired { get; set; } = true;
    public string? Notes { get; set; }
    public List<Guid> SubstituteItemIds { get; set; } = new();
}

public sealed class ServiceRecipeInput
{
    public Guid ItemId { get; set; }
    public string? Notes { get; set; }
    public bool IsActive { get; set; } = true;
    public List<ServiceRecipeItemInput> Items { get; set; } = new();
}

public sealed class CustomerBranchLimit
{
    public Guid Id { get; set; }
    public Guid CustomerId { get; set; }
    public string? CustomerName { get; set; }
    public Guid BranchId { get; set; }
    public string? BranchName { get; set; }
    public decimal CreditLimit { get; set; }
    public decimal SalesLimit { get; set; }
    public bool IsActive { get; set; } = true;
    public int Version { get; set; } = 1;
}

public sealed class CustomerBranchLimitInput
{
    public Guid CustomerId { get; set; }
    public Guid BranchId { get; set; }
    public decimal CreditLimit { get; set; }
    public decimal SalesLimit { get; set; }
    public bool IsActive { get; set; } = true;
}

public sealed class BranchSettings
{
    public Guid BranchId { get; set; }
    public Guid? ConsumablesWarehouseId { get; set; }
    public string? ConsumablesWarehouseName { get; set; }
}

public sealed class BranchSettingsInput
{
    public Guid BranchId { get; set; }
    public Guid? ConsumablesWarehouseId { get; set; }
}

public sealed class PostingDefaultType
{
    public string Key { get; set; } = "";
    public string Module { get; set; } = "";
    public string? PartyType { get; set; }
    public bool IsRequired { get; set; }
    public short SortOrder { get; set; }
    public string LabelAr { get; set; } = "";
    public string? LabelEn { get; set; }
    public bool IsActive { get; set; } = true;
}

