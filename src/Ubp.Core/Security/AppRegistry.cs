namespace Ubp.Core.Security;

public sealed record ScreenAction(string Code, string TitleKey);

public sealed record ScreenDefinition(
    string Code,
    string Route,
    string Icon,
    string TitleKey,
    string GroupKey,
    IReadOnlyList<ScreenAction> Actions,
    string? PermissionGroup = null)
{
    /// <summary>
    /// مفتاح الصلاحية المعلن: accounting.accounts.create_root للمحاسبة،
    /// وللشاشات القديمة يبقى SCREEN/ACTION كما هو في قاعدة البيانات.
    /// </summary>
    public string PermissionKey(string actionCode) =>
        PermissionGroup is null
            ? $"{Code}/{actionCode}"
            : $"{PermissionGroup}.{actionCode.ToLowerInvariant()}";
}

public static class AppRegistry
{
    public const string GroupMain = "group.main";
    public const string GroupDefinitions = "group.definitions";
    public const string GroupSecurity = "group.security";
    public const string GroupSystem = "group.system";
    public const string GroupInventory = "group.inventory";
    public const string GroupPartners = "group.partners";
    public const string GroupReports = "group.reports";
    public const string GroupAccounting = "group.accounting";
    public const string GroupSales = "group.sales";

    private static ScreenAction A(string code) => new(code, $"action.{code.ToLowerInvariant()}");

    public static readonly IReadOnlyList<ScreenDefinition> Screens = new List<ScreenDefinition>
    {
        new(ScreenCodes.Dashboard, "/", "Dashboard", "screen.dashboard", GroupMain,
            new[] { A(ActionCodes.View) }),

        new(ScreenCodes.SettingsGeneral, "/settings/general", "Settings", "screen.settings_general", GroupDefinitions,
            new[] { A(ActionCodes.View), A(ActionCodes.Edit) }),

        new(ScreenCodes.Branches, "/settings/branches", "AccountTree", "screen.branches", GroupDefinitions,
            new[] { A(ActionCodes.View), A(ActionCodes.Add), A(ActionCodes.Edit),
                    A(ActionCodes.Delete), A(ActionCodes.ToggleActive),
                    A(ActionCodes.ManageDefaults) }),

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
            new[] { A(ActionCodes.View), A(ActionCodes.RunMigrations), A(ActionCodes.EditRows) }),

        new(ScreenCodes.Accounts, "/accounting/accounts", "AccountTree", "screen.accounts", GroupAccounting,
            new[] { A(ActionCodes.View), A(ActionCodes.CreateRoot), A(ActionCodes.CreateChild),
                    A(ActionCodes.Edit), A(ActionCodes.ToggleActive), A(ActionCodes.Move),
                    A(ActionCodes.LoadDefaultTree) }, "accounting.accounts"),

        new(ScreenCodes.CostCenters, "/accounting/cost-centers", "Hub", "screen.cost_centers", GroupAccounting,
            new[] { A(ActionCodes.View), A(ActionCodes.CreateRoot), A(ActionCodes.CreateChild),
                    A(ActionCodes.Edit), A(ActionCodes.ToggleActive),
                    A(ActionCodes.Move) }, "accounting.cost_centers"),

        new(ScreenCodes.Journals, "/accounting/journals", "MenuBook", "screen.journals", GroupAccounting,
            new[] { A(ActionCodes.View), A(ActionCodes.Create), A(ActionCodes.Edit),
                    A(ActionCodes.ToggleActive) }, "accounting.journals"),

        new(ScreenCodes.AccountingSettings, "/accounting/settings", "Tune", "screen.accounting_settings",
            GroupAccounting,
            new[] { A(ActionCodes.View), A(ActionCodes.Save), A(ActionCodes.CheckCompleteness),
                    A(ActionCodes.EnableAutoPost) }, "accounting.settings"),

        new(ScreenCodes.JournalEntries, "/accounting/entries", "ReceiptLong", "screen.entries", GroupAccounting,
            new[] { A(ActionCodes.View), A(ActionCodes.Create), A(ActionCodes.Open), A(ActionCodes.Copy),
                    A(ActionCodes.Reverse), A(ActionCodes.CancelEntry) }, "accounting.entries"),

        new(ScreenCodes.JournalEntry, "/accounting/entries/{id:guid}", "EditNote", "screen.entry",
            GroupAccounting,
            new[] { A(ActionCodes.View), A(ActionCodes.SaveDraft), A(ActionCodes.Post),
                    A(ActionCodes.AddLine), A(ActionCodes.DeleteLine), A(ActionCodes.AutoBalance),
                    A(ActionCodes.SelectParty), A(ActionCodes.DistributeCost), A(ActionCodes.Reverse),
                    A(ActionCodes.Print) }, "accounting.entry"),

        new(ScreenCodes.OpeningBalances, "/accounting/opening-balances", "PlaylistAdd",
            "screen.opening_balances", GroupAccounting,
            new[] { A(ActionCodes.View), A(ActionCodes.ImportCsv),
                    A(ActionCodes.AutoBalanceEquity), A(ActionCodes.SaveDraft), A(ActionCodes.Post),
                    A(ActionCodes.OpenEntry) }, "accounting.opening_balances"),

        new(ScreenCodes.StockPostingCenter, "/accounting/stock-posting", "Inventory", "screen.stock_posting",
            GroupAccounting,
            new[] { A(ActionCodes.View), A(ActionCodes.Preview), A(ActionCodes.GenerateSingle),
                    A(ActionCodes.GenerateAll), A(ActionCodes.OpenEntry) }, "accounting.stock_posting"),

        new(ScreenCodes.LedgerReport, "/reports/accounting/ledger", "FormatListNumbered",
            "screen.ledger", GroupReports,
            new[] { A(ActionCodes.View), A(ActionCodes.Print) }, "accounting.ledger"),

        new(ScreenCodes.TrialBalanceReport, "/reports/accounting/trial-balance", "Balance",
            "screen.trial_balance", GroupReports,
            new[] { A(ActionCodes.View), A(ActionCodes.Print) }, "accounting.trial_balance"),

        new(ScreenCodes.PartyStatementReport, "/reports/accounting/party-statement", "Contacts",
            "screen.party_statement", GroupReports,
            new[] { A(ActionCodes.View), A(ActionCodes.Print) }, "accounting.party_statement"),

        new(ScreenCodes.CostCenterReport, "/reports/accounting/cost-centers", "DonutLarge",
            "screen.cost_center_report", GroupReports,
            new[] { A(ActionCodes.View), A(ActionCodes.Print) }, "accounting.cost_centers_report"),

        new(ScreenCodes.StockVsGlReport, "/reports/accounting/stock-vs-gl", "CompareArrows",
            "screen.stock_vs_gl", GroupReports,
            new[] { A(ActionCodes.View), A(ActionCodes.Print) }, "accounting.stock_vs_gl"),

        new(ScreenCodes.Employees, "/settings/employees", "Badge", "screen.employees", GroupDefinitions,
            new[] { A(ActionCodes.View), A(ActionCodes.Create), A(ActionCodes.Edit),
                    A(ActionCodes.ToggleActive), A(ActionCodes.Delete) }, "settings.employees"),

        new(ScreenCodes.Taxes, "/settings/taxes", "Percent", "screen.taxes", GroupDefinitions,
            new[] { A(ActionCodes.View), A(ActionCodes.Create), A(ActionCodes.Edit),
                    A(ActionCodes.ToggleActive), A(ActionCodes.Delete) }, "settings.taxes"),

        new(ScreenCodes.PaymentTerms, "/settings/payment-terms", "Schedule", "screen.payment_terms",
            GroupDefinitions,
            new[] { A(ActionCodes.View), A(ActionCodes.Create), A(ActionCodes.Edit),
                    A(ActionCodes.ToggleActive), A(ActionCodes.Delete) }, "settings.payment_terms"),

        new(ScreenCodes.PriceLists, "/settings/price-lists", "Sell", "screen.price_lists",
            GroupDefinitions,
            new[] { A(ActionCodes.View), A(ActionCodes.Create), A(ActionCodes.Edit),
                    A(ActionCodes.ToggleActive), A(ActionCodes.Delete),
                    A(ActionCodes.ManageItems) }, "settings.price_lists"),

        new(ScreenCodes.ServiceRecipes, "/inventory/recipes", "Blender", "screen.service_recipes",
            GroupInventory,
            new[] { A(ActionCodes.View), A(ActionCodes.Create), A(ActionCodes.Edit),
                    A(ActionCodes.ToggleActive), A(ActionCodes.Delete),
                    A(ActionCodes.EditLines) }, "inventory.recipes"),

        new(ScreenCodes.PurchaseInvoices, "/purchasing/invoices", "Receipt", "screen.purchase_invoices",
            GroupAccounting,
            new[] { A(ActionCodes.View), A(ActionCodes.Create), A(ActionCodes.Edit),
                    A(ActionCodes.Delete), A(ActionCodes.Post) }, "purchasing.invoices"),

        new(ScreenCodes.PurchaseReturns, "/purchasing/returns", "AssignmentReturn",
            "screen.purchase_returns", GroupAccounting,
            new[] { A(ActionCodes.View), A(ActionCodes.Create), A(ActionCodes.Edit),
                    A(ActionCodes.Delete), A(ActionCodes.Post) }, "purchasing.returns"),

        new(ScreenCodes.PurchaseAging, "/purchasing/aging", "HourglassBottom",
            "screen.purchase_aging", GroupReports,
            new[] { A(ActionCodes.View), A(ActionCodes.Print) }, "purchasing.aging"),

        new(ScreenCodes.Treasuries, "/treasury/management", "AccountBalanceWallet",
            "screen.treasuries", GroupAccounting,
            new[] { A(ActionCodes.View), A(ActionCodes.Create), A(ActionCodes.ManageMethods) },
            "treasury.management"),

        new(ScreenCodes.TreasuryVouchers, "/treasury/vouchers", "ReceiptLong",
            "screen.treasury_vouchers", GroupAccounting,
            new[] { A(ActionCodes.View), A(ActionCodes.Create), A(ActionCodes.Edit),
                    A(ActionCodes.Delete), A(ActionCodes.Post), A(ActionCodes.Print),
                    A(ActionCodes.CancelEntry) },
            "treasury.vouchers"),

        new(ScreenCodes.TreasuryShifts, "/treasury/shifts", "PointOfSale",
            "screen.treasury_shifts", GroupAccounting,
            new[] { A(ActionCodes.View), A(ActionCodes.Open), A(ActionCodes.Close),
                    A(ActionCodes.Reopen) },
            "treasury.shifts"),

        new(ScreenCodes.TreasuryReconciliation, "/treasury/reconciliation", "SwapHoriz",
            "screen.treasury_reconciliation", GroupAccounting,
            new[] { A(ActionCodes.View), A(ActionCodes.Post) }, "treasury.reconciliation"),

        new(ScreenCodes.SalesBookings, "/sales/bookings", "EventAvailable", "screen.sales_bookings",
            GroupSales,
            new[] { A(ActionCodes.View), A(ActionCodes.Create), A(ActionCodes.Edit),
                    A(ActionCodes.Confirm), A(ActionCodes.CancelEntry), A(ActionCodes.AssignOthers) },
            "sales.bookings"),

        new(ScreenCodes.SalesAppointments, "/sales/appointments", "CalendarMonth",
            "screen.sales_appointments", GroupSales,
            new[] { A(ActionCodes.View), A(ActionCodes.Manage) }, "sales.appointments"),

        new(ScreenCodes.SalesDeliveries, "/sales/deliveries", "LocalShipping",
            "screen.sales_deliveries", GroupSales,
            new[] { A(ActionCodes.View), A(ActionCodes.Execute), A(ActionCodes.Post) },
            "sales.deliveries"),

        new(ScreenCodes.SalesInvoices, "/sales/invoices", "Receipt", "screen.sales_invoices",
            GroupSales,
            new[] { A(ActionCodes.View), A(ActionCodes.Create), A(ActionCodes.Edit),
                    A(ActionCodes.Post), A(ActionCodes.Print) }, "sales.invoices"),

        new(ScreenCodes.SalesReturns, "/sales/returns", "AssignmentReturn",
            "screen.sales_returns", GroupSales,
            new[] { A(ActionCodes.View), A(ActionCodes.Create), A(ActionCodes.Post),
                    A(ActionCodes.Print) }, "sales.returns"),

        new(ScreenCodes.SalesSettlements, "/sales/settlements", "SwapHoriz",
            "screen.sales_settlements", GroupSales,
            new[] { A(ActionCodes.View), A(ActionCodes.Create), A(ActionCodes.Approve),
                    A(ActionCodes.Post) }, "sales.settlements"),

        new(ScreenCodes.SalesCommissions, "/sales/commissions", "Percent",
            "screen.sales_commissions", GroupSales,
            new[] { A(ActionCodes.View), A(ActionCodes.Calculate) }, "sales.commissions"),

        new(ScreenCodes.AgingReport, "/reports/aging", "HourglassBottom",
            "screen.aging", GroupReports,
            new[] { A(ActionCodes.View), A(ActionCodes.Export) }, "reports.aging"),

        new(ScreenCodes.PostingCenter, "/accounting/posting-center", "FactCheck",
            "screen.posting_center", GroupAccounting,
            new[] { A(ActionCodes.View), A(ActionCodes.ViewUnposted), A(ActionCodes.Reconcile) },
            "accounting.posting"),

        new(ScreenCodes.PosTerminal, "/pos/terminal", "PointOfSale", "screen.pos_terminal",
            GroupSales,
            new[] { A(ActionCodes.View), A(ActionCodes.Checkout) }, "pos.terminal"),

        new(ScreenCodes.PosReceipts, "/pos/receipts", "ReceiptLong", "screen.pos_receipts",
            GroupSales,
            new[] { A(ActionCodes.View), A(ActionCodes.Print) }, "pos.receipts")
    };

    public static readonly IReadOnlyDictionary<string, ScreenDefinition> ByCode =
        Screens.ToDictionary(s => s.Code, StringComparer.OrdinalIgnoreCase);

    public static IEnumerable<ScreenAction> AllActions() =>
        Screens.SelectMany(s => s.Actions.Select(a => a with { Code = $"{s.Code}/{a.Code}" }));

    public static bool IsRegistered(string screenCode, string actionCode) =>
        ByCode.TryGetValue(screenCode, out var screen) &&
        screen.Actions.Any(a => string.Equals(a.Code, actionCode, StringComparison.OrdinalIgnoreCase));
}
