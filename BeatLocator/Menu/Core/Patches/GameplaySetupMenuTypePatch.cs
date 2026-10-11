using System.Reflection;
using BeatSaberMarkupLanguage.GameplaySetup;
using HarmonyLib;

namespace BeatLocator.Menu;

// BSML otherwise treats a custom flow as MenuType.Custom and hides Solo-only
// mod tabs. Change only the visibility query while BeatLocator owns this panel.
[HarmonyPatch]
internal static class GameplaySetupMenuTypePatch
{
    private static MethodBase TargetMethod()
    {
        return AccessTools.Method(
            typeof(BeatSaberMarkupLanguage.GameplaySetup.GameplaySetup).Assembly.GetType(
                "BeatSaberMarkupLanguage.GameplaySetup.GameplaySetupMenu"),
            "IsMenuType");
    }

    private static void Prefix(ref MenuType toCheck)
    {
        if (toCheck == MenuType.Custom && BeatLocatorFlowCoordinator.IsGameplaySettingsVisible)
            toCheck = MenuType.Solo;
    }
}
