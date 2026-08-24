using HarmonyLib;
using System;
using System.Reflection;
using System.Threading.Tasks;
using BeatLocator.PostLevel;

namespace BeatLocator.Menu;

/// <summary>
/// Delays the vanilla Results Continue action only while a BeatLocator roulette
/// run is active. This lets the menu fade become fully opaque before Beat Saber
/// starts restoring its Solo song-selection flow.
/// </summary>
[HarmonyPatch(typeof(ResultsViewController), "ContinueButtonPressed")]
internal static class ResultsContinuePatch
{
    private static PostLevelMenuPresenter? _presenter;
    private static bool _releaseAutomaticFailedContinue;
    private static bool _suppressAutomaticFailedContinueEvent;
    private static bool _automaticFailedContinueInProgress;

    internal static MethodBase GetContinueMethod()
    {
        return AccessTools.Method(typeof(ResultsViewController), "ContinueButtonPressed")
               ?? throw new MissingMethodException(
                   nameof(ResultsViewController),
                   "ContinueButtonPressed");
    }

    internal static void Register(PostLevelMenuPresenter presenter)
    {
        _presenter = presenter;
    }

    internal static void Unregister(PostLevelMenuPresenter presenter)
    {
        if (ReferenceEquals(_presenter, presenter))
        {
            _presenter = null;
        }
    }

    internal static bool ConsumeAutomaticFailedContinueEvent()
    {
        if (!_suppressAutomaticFailedContinueEvent) return false;
        _suppressAutomaticFailedContinueEvent = false;
        return true;
    }

    internal static void AdvanceFailedResults(ResultsViewController resultsViewController)
    {
        if (_automaticFailedContinueInProgress ||
            PostLevelUiState.Instance?.HasFailedTerminalForActiveRun() != true)
        {
            return;
        }

        _automaticFailedContinueInProgress = true;
        AdvanceFailedResultsAsync(resultsViewController);
    }

    private static async void AdvanceFailedResultsAsync(
        ResultsViewController resultsViewController)
    {
        try
        {
            // Finish Results activation before asking its own coordinator to
            // leave. Results belongs to a newly loaded menu scene with its own
            // FadeInOutController, so black must be applied again here.
            await Task.Yield();
            await BeatLocatorFlowCoordinator
                .FadeOutActiveForPostLevelTransitionAsync();
            _releaseAutomaticFailedContinue = true;
            _suppressAutomaticFailedContinueEvent = true;
            Plugin.Log.Info(
                "[PP UI] Automatically skipping vanilla failed Results behind the fade.");
            GetContinueMethod().Invoke(resultsViewController, System.Array.Empty<object>());
            // The menu presenter may not exist in this scene. Do not let the
            // one-shot event suppression leak into a later completed run.
            _suppressAutomaticFailedContinueEvent = false;
            BeatLocatorFlowCoordinator.PresentPendingFailedTerminalWhenSoloReady();
        }
        catch (System.Exception exception)
        {
            _releaseAutomaticFailedContinue = false;
            _suppressAutomaticFailedContinueEvent = false;
            BeatLocatorFlowCoordinator.RecoverActivePostLevelTransition();
            Plugin.Log.Error(
                $"[PP UI] Could not skip vanilla failed Results: {exception}");
        }
        finally
        {
            _releaseAutomaticFailedContinue = false;
            _automaticFailedContinueInProgress = false;
        }
    }

    [HarmonyPrefix]
    private static bool Prefix(
        ResultsViewController __instance,
        MethodBase __originalMethod)
    {
        if (_releaseAutomaticFailedContinue)
        {
            _releaseAutomaticFailedContinue = false;
            return true;
        }

        return _presenter?.AllowContinueOrBeginFade(
                   __instance,
                   __originalMethod) ?? true;
    }
}
