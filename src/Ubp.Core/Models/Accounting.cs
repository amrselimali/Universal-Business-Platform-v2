namespace Ubp.Core.Models;

public static class AccountTypes
{
    public const string AssetReceivable = "asset_receivable";
    public const string AssetCash = "asset_cash";
    public const string AssetCurrent = "asset_current";
    public const string AssetFixed = "asset_fixed";
    public const string LiabilityPayable = "liability_payable";
    public const string LiabilityCurrent = "liability_current";
    public const string Equity = "equity";
    public const string Income = "income";
    public const string Expense = "expense";
    public const string ExpenseCogs = "expense_cogs";

    public static readonly string[] All =
    {
        AssetReceivable, AssetCash, AssetCurrent, AssetFixed, LiabilityPayable,
        LiabilityCurrent, Equity, Income, Expense, ExpenseCogs
    };

    public static bool RequiresCustomer(string type) => type == AssetReceivable;
    public static bool RequiresSupplier(string type) => type == LiabilityPayable;

    public static bool RequiresParty(string type) => RequiresCustomer(type) || RequiresSupplier(type);

    public static bool IsDebitNature(string type) =>
        type is AssetReceivable or AssetCash or AssetCurrent or AssetFixed or Expense or ExpenseCogs;
}

public static class CostCenterPolicies
{
    public const short NotAllowed = 0;
    public const short Optional = 1;
    public const short Required = 2;
}

public static class JournalTypes
{
    public const string General = "general";
    public const string Sales = "sales";
    public const string Purchase = "purchase";
    public const string Cash = "cash";
    public const string Bank = "bank";
    public const string Stock = "stock";
    public const string Opening = "opening";

    public static readonly string[] All =
        { General, Sales, Purchase, Cash, Bank, Stock, Opening };
}

public static class PostingKeys
{
    public const string StockInventory = "stock_inventory";
    public const string StockCogs = "stock_cogs";
    public const string StockAdjustmentGain = "stock_adjustment_gain";
    public const string StockAdjustmentLoss = "stock_adjustment_loss";
    public const string OpeningEquity = "opening_equity";
    public const string Rounding = "rounding";
    public const string SalesRevenue = "sales_revenue";
    public const string VatOutput = "vat_output";
    public const string VatInput = "vat_input";
    public const string CustomerReceivable = "customer_receivable";
    public const string SupplierPayable = "supplier_payable";
    public const string PurchaseVariance = "purchase_variance";
    public const string RoundingDifference = "rounding_difference";
    public const string CustomerAdvances = "customer_advances";
    public const string SupplierAdvances = "supplier_advances";
    public const string PaymentFees = "payment_fees";
    public const string CashOverShort = "cash_over_short";
    public const string InterBranchClearing = "inter_branch_clearing";
    public const string BookingForfeitIncome = "booking_forfeit_income";

    public static readonly string[] All =
    {
        StockInventory, StockCogs, StockAdjustmentGain, StockAdjustmentLoss,
        OpeningEquity, Rounding, SalesRevenue, VatOutput, VatInput, CustomerReceivable,
        SupplierPayable, PurchaseVariance, RoundingDifference, CustomerAdvances,
        SupplierAdvances, PaymentFees, CashOverShort, InterBranchClearing, BookingForfeitIncome
    };
}

public sealed class Account
{
    public Guid Id { get; set; }
    public string Code { get; set; } = "";
    public string NameAr { get; set; } = "";
    public string? NameEn { get; set; }
    public Guid? ParentId { get; set; }
    public string Path { get; set; } = "/";
    public short Level { get; set; } = 1;
    public bool IsGroup { get; set; }
    public string AccountType { get; set; } = AccountTypes.AssetCurrent;
    public short CostCenterPolicy { get; set; }
    public bool IsSystem { get; set; }
    public string? Notes { get; set; }
    public bool IsActive { get; set; } = true;
    public int Version { get; set; } = 1;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
    public int ChildrenCount { get; set; }
    public int LinesCount { get; set; }

    public bool RequiresCustomer => AccountTypes.RequiresCustomer(AccountType);
    public bool RequiresSupplier => AccountTypes.RequiresSupplier(AccountType);
    public bool RequiresParty => AccountTypes.RequiresParty(AccountType);

    public string DisplayName(bool english) =>
        english && !string.IsNullOrWhiteSpace(NameEn) ? NameEn! : NameAr;
}

public sealed class AccountInput
{
    public string Code { get; set; } = "";
    public string NameAr { get; set; } = "";
    public string? NameEn { get; set; }
    public Guid? ParentId { get; set; }
    public bool IsGroup { get; set; }
    public string AccountType { get; set; } = AccountTypes.AssetCurrent;
    public short CostCenterPolicy { get; set; } = CostCenterPolicies.Optional;
    public string? Notes { get; set; }
    public bool IsActive { get; set; } = true;
}


public sealed class CostCenter
{
    public Guid Id { get; set; }
    public string Code { get; set; } = "";
    public string NameAr { get; set; } = "";
    public string? NameEn { get; set; }
    public Guid? ParentId { get; set; }
    public string Path { get; set; } = "/";
    public short Level { get; set; } = 1;
    public bool IsGroup { get; set; }
    public Guid? BranchId { get; set; }
    public string? BranchName { get; set; }
    public string? Notes { get; set; }
    public bool IsActive { get; set; } = true;
    public int Version { get; set; } = 1;
    public int ChildrenCount { get; set; }

    public string DisplayName(bool english) =>
        english && !string.IsNullOrWhiteSpace(NameEn) ? NameEn! : NameAr;
}

public sealed class CostCenterInput
{
    public string Code { get; set; } = "";
    public string NameAr { get; set; } = "";
    public string? NameEn { get; set; }
    public Guid? ParentId { get; set; }
    public bool IsGroup { get; set; }
    public Guid? BranchId { get; set; }
    public string? Notes { get; set; }
    public bool IsActive { get; set; } = true;
}

public sealed class Journal
{
    public Guid Id { get; set; }
    public string Code { get; set; } = "";
    public string NameAr { get; set; } = "";
    public string? NameEn { get; set; }
    public string JournalType { get; set; } = JournalTypes.General;
    public Guid? DefaultAccountId { get; set; }
    public string? DefaultAccountName { get; set; }
    public bool AllowManual { get; set; } = true;
    public bool IsSystem { get; set; }
    public int SortOrder { get; set; }
    public bool IsActive { get; set; } = true;
    public int Version { get; set; } = 1;
    public int EntriesCount { get; set; }

    public string DisplayName(bool english) =>
        english && !string.IsNullOrWhiteSpace(NameEn) ? NameEn! : NameAr;
}

public sealed class JournalInput
{
    public string Code { get; set; } = "";
    public string NameAr { get; set; } = "";
    public string? NameEn { get; set; }
    public string JournalType { get; set; } = JournalTypes.General;
    public Guid? DefaultAccountId { get; set; }
    public bool AllowManual { get; set; } = true;
    public int SortOrder { get; set; }
    public bool IsActive { get; set; } = true;
}

public sealed class PostingDefault
{
    public Guid Id { get; set; }
    public string SettingKey { get; set; } = "";
    public Guid? AccountId { get; set; }
    public string? AccountCode { get; set; }
    public string? AccountName { get; set; }
    public bool IsActive { get; set; } = true;
    public int Version { get; set; }
}

public sealed class ItemCategoryAccount
{
    public Guid Id { get; set; }
    public Guid CategoryId { get; set; }
    public string? CategoryCode { get; set; }
    public string? CategoryName { get; set; }
    public Guid InventoryAccountId { get; set; }
    public Guid CogsAccountId { get; set; }
    public Guid? AdjustmentGainAccountId { get; set; }
    public Guid? AdjustmentLossAccountId { get; set; }
    public string? InventoryAccountName { get; set; }
    public string? CogsAccountName { get; set; }
    public int Version { get; set; }
}

public sealed class ItemCategoryAccountInput
{
    public Guid CategoryId { get; set; }
    public Guid InventoryAccountId { get; set; }
    public Guid CogsAccountId { get; set; }
    public Guid? AdjustmentGainAccountId { get; set; }
    public Guid? AdjustmentLossAccountId { get; set; }
}

public sealed class AccountingSettings
{
    public Guid Id { get; set; }
    public bool AutoPostStockJournals { get; set; }
    public DateTime? AccountingStartDate { get; set; }
    public int Version { get; set; }
}

public sealed class CompletenessItem
{
    public string Code { get; set; } = "";
    public string LabelKey { get; set; } = "";
    public bool Ok { get; set; }
    public bool Blocking { get; set; } = true;
    public string? Detail { get; set; }
}

public sealed class CompletenessReport
{
    public List<CompletenessItem> Items { get; set; } = new();
    public bool IsComplete => Items.All(i => i.Ok || !i.Blocking);
    public int BlockingCount => Items.Count(i => !i.Ok && i.Blocking);
}
