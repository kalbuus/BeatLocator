using HarmonyLib;
using HMUI;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace BeatLocator.Menu;

// Observe vanilla navigation rather than depending on Heck's internal types.
// This also covers pre-play screens presented after Play's handler returns.
[HarmonyPatch(typeof(SinglePlayerLevelSelectionFlowCoordinator),
    "LevelSelectionFlowCoordinatorTopViewControllerWillChange")]
internal static class LaunchPromptFadePatch
{
    [HarmonyPostfix]
    private static void Postfix(
        SinglePlayerLevelSelectionFlowCoordinator __instance,
        ViewController newViewController)
    {
        BeatLocatorFlowCoordinator.RevealLaunchPrompt(__instance, newViewController);
    }
}

[HarmonyPatch]
internal static class LaunchGameplayFadePatch
{
    [HarmonyTargetMethods]
    private static IEnumerable<MethodBase> TargetMethods()
    {
        return typeof(MenuTransitionsHelper)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Where(method => method.Name == "StartStandardLevel");
    }

    [HarmonyPrefix]
    private static void Prefix()
    {
        BeatLocatorFlowCoordinator.ReleaseLaunchFadeForGameplay();
    }
}
