using BeatLocator.PostLevel;
using HarmonyLib;
using System;
using System.Reflection;

namespace BeatLocator.Menu;

/// <summary>
/// Covers the vanilla pause-menu exit before it begins restoring the menu.
/// Normal levels are never intercepted.
/// </summary>
[HarmonyPatch(typeof(PauseMenuManager), "MenuButtonPressed")]
internal static class PauseMenuContinuePatch
{
    private static readonly FieldInfo? InitDataField =
        AccessTools.Field(typeof(PauseMenuManager), "_initData");
    private static bool _releaseVanillaMenu;
    private static bool _fadeInProgress;

    [HarmonyPrefix]
    private static bool Prefix(
        PauseMenuManager __instance,
        MethodBase __originalMethod)
    {
        if (_releaseVanillaMenu)
        {
            _releaseVanillaMenu = false;
            return true;
        }

        if (_fadeInProgress) return false;

        var sessionManager = RoulettePlaySessionManager.Instance;
        var initData = InitDataField?.GetValue(__instance) as PauseMenuManager.InitData;
        if (sessionManager == null ||
            initData == null ||
            !sessionManager.MatchesActive(initData.beatmapKey))
        {
            if (sessionManager != null && initData != null)
            {
                sessionManager.CancelIfActiveRunDoesNotMatch(initData.beatmapKey);
            }
            return true;
        }

        _fadeInProgress = true;
        ReleaseMenuAfterFadeAsync(__instance, __originalMethod);
        return false;
    }

    private static async void ReleaseMenuAfterFadeAsync(
        PauseMenuManager pauseMenuManager,
        MethodBase originalMethod)
    {
        try
        {
            await BeatLocatorFlowCoordinator
                .FadeOutActiveForPostLevelTransitionAsync();
            Plugin.Log.Info(
                "[PP UI] Pause Menu exit is black; releasing the vanilla Menu action.");

            _releaseVanillaMenu = true;
            originalMethod.Invoke(pauseMenuManager, Array.Empty<object>());
            _releaseVanillaMenu = false;
        }
        catch (Exception exception)
        {
            _releaseVanillaMenu = false;
            BeatLocatorFlowCoordinator.RecoverActivePostLevelTransition();
            Plugin.Log.Error(
                $"[PP UI] Could not release Pause Menu after fading out: {exception}");
        }
        finally
        {
            _fadeInProgress = false;
        }
    }
}
