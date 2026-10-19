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
        "Science" => Icons.Material.Filled.Science,
        "Storage" => Icons.Material.Filled.Storage,
        _ => Icons.Material.Filled.Circle
    };
}
