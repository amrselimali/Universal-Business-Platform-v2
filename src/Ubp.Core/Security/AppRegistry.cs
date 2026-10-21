namespace Ubp.Core.Security;

public static class PermissionScope
{
    public const short Own = 0;
    public const short Branches = 1;
    public const short All = 2;

    public static short Normalize(short value) => value is >= 0 and <= 2 ? value : Branches;
    public static short Max(short a, short b) => Normalize(a) >= Normalize(b) ? Normalize(a) : Normalize(b);
}

public static class ScreenCodes
{
    public const string Dashboard = "DASHBOARD";
    public const string SettingsGeneral = "SETTINGS_GENERAL";
    public const string Branches = "BRANCHES";
    public const string Warehouses = "WAREHOUSES";
    public const string Currencies = "CURRENCIES";
    public const string CustomFields = "CUSTOM_FIELDS";
    public const string Users = "USERS";
    public const string Roles = "ROLES";
    public const string AuditLog = "AUDIT_LOG";
    public const string DatabaseAdmin = "DATABASE_ADMIN";
    public const string QaCenter = "QA_CENTER";
    public const string Units = "UNITS";
    public const string ItemCategories = "ITEM_CATEGORIES";
    public const string Items = "ITEMS";
    public const string Customers = "CUSTOMERS";
    public const string Suppliers = "SUPPLIERS";
    public const string StockDocuments = "STOCK_DOCUMENTS";
    public const string Stocktake = "STOCKTAKE";
    public const string FinancialPeriods = "FINANCIAL_PERIODS";
    public const string StockReports = "STOCK_REPORTS";
}

public static class ActionCodes
{
    public const string View = "VIEW";
    public const string Add = "ADD";
    public const string Edit = "EDIT";
    public const string Delete = "DELETE";
    public const string ToggleActive = "TOGGLE_ACTIVE";
    public const string LinkBranches = "LINK_BRANCHES";
    public const string ManageBranches = "MANAGE_BRANCHES";
    public const string ManageWarehouses = "MANAGE_WAREHOUSES";
    public const string ManageRoles = "MANAGE_ROLES";
    public const string ManagePermissions = "MANAGE_PERMISSIONS";
    public const string ResetPassword = "RESET_PASSWORD";
    public const string Unlock = "UNLOCK";
    public const string RunMigrations = "RUN_MIGRATIONS";
    public const string EditRows = "EDIT_ROWS";
    public const string RunTests = "RUN_TESTS";
    public const string Post = "POST";
    public const string Reverse = "REVERSE";
    public const string Receive = "RECEIVE";
    public const string ClosePeriod = "CLOSE_PERIOD";
    public const string ReopenPeriod = "REOPEN_PERIOD";
}

public sealed record ScreenAction(string Code, string TitleKey);

public sealed record ScreenDefinition(
    string Code,
    string Route,
    string Icon,
    string TitleKey,
    string GroupKey,
    IReadOnlyList<ScreenAction> Actions);

public static class AppRegistry
{
    public const string GroupMain = "group.main";
    public const string GroupDefinitions = "group.definitions";
    public const string GroupSecurity = "group.security";
    public const string GroupSystem = "group.system";
    public const string GroupInventory = "group.inventory";
    public const string GroupPartners = "group.partners";
    public const string GroupReports = "group.reports";

    private static ScreenAction A(string code) => new(code, $"action.{code.ToLowerInvariant()}");

    public static readonly IReadOnlyList<ScreenDefinition> Screens = new List<ScreenDefinition>
    {
        new(ScreenCodes.Dashboard, "/", "Dashboard", "screen.dashboard", GroupMain,
            new[] { A(ActionCodes.View) }),

        new(ScreenCodes.SettingsGeneral, "/settings/general", "Settings", "screen.settings_general", GroupDefinitions,
            new[] { A(ActionCodes.View), A(ActionCodes.Edit) }),

        new(ScreenCodes.Branches, "/settings/branches", "AccountTree", "screen.branches", GroupDefinitions,
            new[] { A(ActionCodes.View), A(ActionCodes.Add), A(ActionCodes.Edit),
                    A(ActionCodes.Delete), A(ActionCodes.ToggleActive) }),

        new(ScreenCodes.Warehouses, "/settings/warehouses", "Warehouse", "screen.warehouses", GroupDefinitions,
            new[] { A(ActionCodes.View), A(ActionCodes.Add), A(ActionCodes.Edit), A(ActionCodes.Delete),
                    A(ActionCodes.ToggleActive), A(ActionCodes.LinkBranches) }),

        new(ScreenCodes.Currencies, "/settings/currencies", "Paid", "screen.currencies", GroupDefinitions,
            new[] { A(ActionCodes.View), A(ActionCodes.Add), A(ActionCodes.Edit), A(ActionCodes.Delete) }),

        new(ScreenCodes.CustomFields, "/settings/custom-fields", "Tune", "screen.custom_fields", GroupDefinitions,
            new[] { A(ActionCodes.View), A(ActionCodes.Add), A(ActionCodes.Edit), A(ActionCodes.Delete) }),

        new(ScreenCodes.Units, "/inventory/units", "Straighten", "screen.units", GroupInventory,
            new[] { A(ActionCodes.View), A(ActionCodes.Add), A(ActionCodes.Edit),
                    A(ActionCodes.Delete), A(ActionCodes.ToggleActive) }),

        new(ScreenCodes.ItemCategories, "/inventory/item-categories", "Category", "screen.item_categories", GroupInventory,
            new[] { A(ActionCodes.View), A(ActionCodes.Add), A(ActionCodes.Edit),
                    A(ActionCodes.Delete), A(ActionCodes.ToggleActive) }),

        new(ScreenCodes.Items, "/inventory/items", "Inventory2", "screen.items", GroupInventory,
            new[] { A(ActionCodes.View), A(ActionCodes.Add), A(ActionCodes.Edit),
                    A(ActionCodes.Delete), A(ActionCodes.ToggleActive) }),

        new(ScreenCodes.StockDocuments, "/inventory/documents", "Description", "screen.stock_documents", GroupInventory,
            new[] { A(ActionCodes.View), A(ActionCodes.Add), A(ActionCodes.Edit),
                    A(ActionCodes.Delete), A(ActionCodes.Post), A(ActionCodes.Reverse), A(ActionCodes.Receive) }),

        new(ScreenCodes.Stocktake, "/inventory/stocktake", "FactCheck", "screen.stocktake", GroupInventory,
            new[] { A(ActionCodes.View), A(ActionCodes.Add), A(ActionCodes.Edit),
                    A(ActionCodes.Delete), A(ActionCodes.Post) }),

        new(ScreenCodes.Customers, "/partners/customers", "People", "screen.customers", GroupPartners,
            new[] { A(ActionCodes.View), A(ActionCodes.Add), A(ActionCodes.Edit),
                    A(ActionCodes.Delete), A(ActionCodes.ToggleActive) }),

        new(ScreenCodes.Suppliers, "/partners/suppliers", "LocalShipping", "screen.suppliers", GroupPartners,
            new[] { A(ActionCodes.View), A(ActionCodes.Add), A(ActionCodes.Edit),
                    A(ActionCodes.Delete), A(ActionCodes.ToggleActive) }),

        new(ScreenCodes.FinancialPeriods, "/accounting/periods", "EventNote", "screen.periods", GroupDefinitions,
            new[] { A(ActionCodes.View), A(ActionCodes.Add), A(ActionCodes.Edit), A(ActionCodes.Delete),
                    A(ActionCodes.ClosePeriod), A(ActionCodes.ReopenPeriod) }),

        new(ScreenCodes.StockReports, "/reports/stock", "Assessment", "screen.stock_reports", GroupReports,
            new[] { A(ActionCodes.View) }),

        new(ScreenCodes.Users, "/users", "ManageAccounts", "screen.users", GroupSecurity,
            new[] { A(ActionCodes.View), A(ActionCodes.Add), A(ActionCodes.Edit), A(ActionCodes.Delete),
                    A(ActionCodes.ManageBranches), A(ActionCodes.ManageWarehouses), A(ActionCodes.ManageRoles),
                    A(ActionCodes.ManagePermissions), A(ActionCodes.ResetPassword), A(ActionCodes.Unlock) }),

        new(ScreenCodes.Roles, "/roles", "AdminPanelSettings", "screen.roles", GroupSecurity,
            new[] { A(ActionCodes.View), A(ActionCodes.Add), A(ActionCodes.Edit),
                    A(ActionCodes.Delete), A(ActionCodes.ManagePermissions) }),

        new(ScreenCodes.AuditLog, "/audit", "History", "screen.audit", GroupSecurity,
            new[] { A(ActionCodes.View) }),

        new(ScreenCodes.QaCenter, "/qa", "Science", "screen.qa", GroupSystem,
            new[] { A(ActionCodes.View), A(ActionCodes.RunTests) }),

        new(ScreenCodes.DatabaseAdmin, "/admin/database", "Storage", "screen.database_admin", GroupSystem,
            new[] { A(ActionCodes.View), A(ActionCodes.RunMigrations), A(ActionCodes.EditRows) })
    };

    public static readonly IReadOnlyDictionary<string, ScreenDefinition> ByCode =
        Screens.ToDictionary(s => s.Code, StringComparer.OrdinalIgnoreCase);

    public static IEnumerable<ScreenAction> AllActions() =>
        Screens.SelectMany(s => s.Actions.Select(a => a with { Code = $"{s.Code}/{a.Code}" }));

    public static bool IsRegistered(string screenCode, string actionCode) =>
        ByCode.TryGetValue(screenCode, out var screen) &&
        screen.Actions.Any(a => string.Equals(a.Code, actionCode, StringComparison.OrdinalIgnoreCase));
}
