using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace BeatLocator.Menu;

/// <summary>
/// Vanilla navigation tries to reveal Results and Solo while BeatLocator is
/// replacing them. Keep the global HMD/desktop fade black until the destination
/// view is active, then BeatLocator's own FadeIn is allowed through.
/// </summary>
[HarmonyPatch]
internal static class PostLevelFadeInPatch
{
    [HarmonyTargetMethods]
    private static IEnumerable<MethodBase> TargetMethods()
    {
        var fadeType = AccessTools.TypeByName("FadeInOutController")
            ?? throw new TypeLoadException("FadeInOutController was not found.");
        return fadeType
            .GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Where(method => method.Name == "FadeIn");
    }

    [HarmonyPrefix]
    private static bool Prefix(object[] __args)
    {
        if (!BeatLocatorFlowCoordinator.ShouldHoldPostLevelBlack)
        {
            return true;
        }

        // Preserve navigation callbacks even though the visual reveal is held.
        foreach (var callback in __args.OfType<Action>())
        {
            callback.Invoke();
        }

        return false;
    }
}
