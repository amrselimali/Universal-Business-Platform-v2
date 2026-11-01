using MudBlazor;

namespace Ubp.Web.Components.Shared;

public static class AppIcons
{
    public static string Resolve(string key) => key switch
    {
        "Dashboard" => Icons.Material.Filled.SpaceDashboard,
        "Settings" => Icons.Material.Filled.Tune,
        "AccountTree" => Icons.Material.Filled.AccountTree,
        "Warehouse" => Icons.Material.Filled.Warehouse,
        "Paid" => Icons.Material.Filled.Paid,
        "Tune" => Icons.Material.Filled.DynamicForm,
        "ManageAccounts" => Icons.Material.Filled.ManageAccounts,
        "AdminPanelSettings" => Icons.Material.Filled.AdminPanelSettings,
        "History" => Icons.Material.Filled.History,
        "MenuBook" => Icons.Material.Filled.MenuBook,
        "Science" => Icons.Material.Filled.Science,
        "Storage" => Icons.Material.Filled.Storage,
        "Straighten" => Icons.Material.Filled.Straighten,
        "Category" => Icons.Material.Filled.Category,
        "Inventory2" => Icons.Material.Filled.Inventory2,
        "Description" => Icons.Material.Filled.Description,
        "FactCheck" => Icons.Material.Filled.FactCheck,
        "People" => Icons.Material.Filled.People,
        "LocalShipping" => Icons.Material.Filled.LocalShipping,
        "EventNote" => Icons.Material.Filled.EventNote,
        "Assessment" => Icons.Material.Filled.Assessment,
        _ => Icons.Material.Filled.Circle
    };
}
