using HarmonyLib;
using BeatLocator.PostLevel;

namespace BeatLocator.Menu;

[HarmonyPatch(
    typeof(ResultsViewController),
    "DidActivate",
    new[] { typeof(bool), typeof(bool), typeof(bool) })]
internal static class FailedResultsActivationPatch
{
    [HarmonyPostfix]
    private static void Postfix(
        ResultsViewController __instance,
        bool addedToHierarchy)
    {
        if (addedToHierarchy &&
            PostLevelUiState.Instance?.HasFailedTerminalForActiveRun() == true)
        {
            ResultsContinuePatch.AdvanceFailedResults(__instance);
        }
    }
}
