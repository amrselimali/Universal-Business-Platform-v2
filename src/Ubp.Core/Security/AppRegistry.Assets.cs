using static Ubp.Core.Security.ScreenCodes;
using static Ubp.Core.Security.ActionCodes;

namespace Ubp.Core.Security;

/// <summary>شاشات الأصول الثابتة والصيانة (8أ) — مفصولة للحفاظ على سقف 300 سطر.</summary>
public static partial class AppRegistry
{
    private static ScreenAction AA(string code) => new(code, $"action.{code.ToLowerInvariant()}");

    private static IReadOnlyList<ScreenDefinition> AssetScreens => new List<ScreenDefinition>
    {
        new(Assets, "/assets/management", "Inventory2", "screen.assets", GroupAccounting,
            new[] { AA(View), AA(Add), AA(Edit), AA(AddMeterReading), AA(OpenMaintenance),
                    AA(CloseMaintenance), AA(RunDepreciation) }),

        new(AssetMaintenance, "/assets/maintenance", "Build", "screen.asset_maintenance",
            GroupAccounting, new[] { AA(View), AA(OpenMaintenance), AA(CloseMaintenance) }),

        new(AssetDepreciation, "/assets/depreciation", "TrendingDown", "screen.asset_depreciation",
            GroupAccounting, new[] { AA(View), AA(RunDepreciation) })
    };
}
