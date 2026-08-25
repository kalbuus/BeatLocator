using BeatLocator.PostLevel;
using IPA.Utilities.Async;
using System;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using Zenject;

namespace BeatLocator.Menu;

internal sealed class PostLevelMenuPresenter : IInitializable, IDisposable
{
    private const float ResultsDeactivationTimeoutSeconds = 5f;
    private const float SoloActivationTimeoutSeconds = 10f;
    private const int SoloSettleDelayMilliseconds = 450;
    private const int TerminalPresentationWatchdogMilliseconds = 3000;

    private readonly ResultsViewController _resultsViewController;
    private readonly SoloFreePlayFlowCoordinator _soloFreePlayFlowCoordinator;
    private readonly PostLevelUiState _uiState;
    private readonly BeatLocatorFlowCoordinator _flowCoordinator;
    private bool _continueInterceptInProgress;
    private bool _allowNextVanillaContinue;
    private bool _quitPresentationWaitInProgress;
    private bool _disposed;

    public PostLevelMenuPresenter(
        ResultsViewController resultsViewController,
        SoloFreePlayFlowCoordinator soloFreePlayFlowCoordinator,
        PostLevelUiState uiState,
        BeatLocatorFlowCoordinator flowCoordinator)
    {
        _resultsViewController = resultsViewController;
        _soloFreePlayFlowCoordinator = soloFreePlayFlowCoordinator;
        _uiState = uiState;
        _flowCoordinator = flowCoordinator;
    }

    public void Initialize()
    {
        _disposed = false;
        ResultsContinuePatch.Register(this);
        _resultsViewController.continueButtonPressedEvent += HandleVanillaContinue;
        _uiState.ReadyChanged += HandleReadyChanged;

        // A terminal result can be produced while MenuInstaller is being rebuilt
        // after gameplay, when no presenter is subscribed to ReadyChanged yet.
        // Consume that app-scoped pending state as soon as the menu presenter exists.
        TryPresentReadyResult();
    }

    internal bool AllowContinueOrBeginFade(
        ResultsViewController resultsViewController,
        MethodBase originalContinueMethod)
    {
        if (!ReferenceEquals(resultsViewController, _resultsViewController))
        {
            return true;
        }

        if (_allowNextVanillaContinue)
        {
            _allowNextVanillaContinue = false;
            return true;
        }

        if (_continueInterceptInProgress)
        {
            return false;
        }

        if (!_uiState.ShouldInterceptVanillaContinue())
        {
            return true;
        }

        _continueInterceptInProgress = true;
        ContinueAfterFadeAsync(originalContinueMethod);
        return false;
    }

    private async void ContinueAfterFadeAsync(MethodBase originalContinueMethod)
    {
        try
        {
            await _flowCoordinator.FadeOutForPostLevelTransitionAsync();
            Plugin.Log.Info(
                "[PP UI] Screen is black; releasing the intercepted Results Continue action.");

            if (!_uiState.ShouldInterceptVanillaContinue())
            {
                _flowCoordinator.FadeInAfterPostLevelTransition();
            }

            _allowNextVanillaContinue = true;
            originalContinueMethod.Invoke(
                _resultsViewController,
                Array.Empty<object>());
            // Harmony normally consumes this bypass synchronously. Clear it
            // defensively so a failed reflection/event path cannot leak into
            // the next completed level's Continue press.
            _allowNextVanillaContinue = false;
        }
        catch (Exception exception)
        {
            _allowNextVanillaContinue = false;
            _flowCoordinator.FadeInAfterPostLevelTransition();
            Plugin.Log.Error(
                $"[PP UI] Could not release Results Continue after fading out: {exception}");
        }
        finally
        {
            _continueInterceptInProgress = false;
        }
    }

    private async void HandleVanillaContinue(ResultsViewController _)
    {
        if (ResultsContinuePatch.ConsumeAutomaticFailedContinueEvent())
        {
            return;
        }

        if (!_uiState.MarkVanillaContinuePressed(out var runId, out var provider))
        {
            TryPresentReadyResult();
            return;
        }

        TryPresentReadyResult();
        var fadeTask = _flowCoordinator.FadeOutForPostLevelTransitionAsync();

        // Continue first lets Beat Saber replace ResultsViewController with its
        // normal Solo screen. Starting a second FlowCoordinator transition while
        // that replacement is still active corrupts the shared screen system.
        var waitStartedAt = Time.realtimeSinceStartup;
        while (_resultsViewController.gameObject.activeInHierarchy)
        {
            if (Time.realtimeSinceStartup - waitStartedAt >=
                ResultsDeactivationTimeoutSeconds)
            {
                Plugin.Log.Error(
                    $"[PP UI] Results screen stayed active for more than " +
                    $"{ResultsDeactivationTimeoutSeconds:0.#} seconds; " +
                    "post-level navigation was canceled to preserve the menu state.");
                _flowCoordinator.FadeInAfterPostLevelTransition();
                return;
            }

            await Task.Yield();
        }

        await fadeTask;
        Plugin.Log.Info("[PP UI] Vanilla Results transition finished.");
        await _flowCoordinator.PresentPostLevelLoadingAsync(runId, provider);
    }

    private void HandleReadyChanged()
    {
        var presentationTask = UnityMainThreadTaskScheduler.Factory
            .StartNew(TryPresentReadyResult);
        presentationTask.ContinueWith(
            task => Plugin.Log.Error(
                $"Could not present BeatLocator's PP result screen: {task.Exception}"),
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted,
            TaskScheduler.Default);
    }

    private void TryPresentReadyResult()
    {
        if (_disposed)
        {
            return;
        }

        // Failed Results are advanced by FailedResultsActivationPatch and then
        // claimed by the active flow coordinator. A menu-scoped presenter can
        // disappear during that transition, so it owns only pause-menu Quit.
        // Do not claim the pending result until Solo is stable: the presenter
        // that saw the gameplay completion event belongs to the old menu scope
        // and is normally disposed while Beat Saber restores the main menu.
        if (_uiState.HasQuitTerminalForActiveRun())
        {
            WaitForStableSoloAndClaimTerminal();
            return;
        }

        if (_uiState.TryTakeReady(out var result) && result != null)
        {
            _flowCoordinator.PresentPostLevelResult(result);
        }
    }

    private async void WaitForStableSoloAndClaimTerminal()
    {
        if (_quitPresentationWaitInProgress || _disposed)
        {
            return;
        }

        _quitPresentationWaitInProgress = true;
        try
        {
            await ClaimAndPresentTerminalWhenSoloStableAsync();
        }
        finally
        {
            _quitPresentationWaitInProgress = false;
        }
    }

    private async Task ClaimAndPresentTerminalWhenSoloStableAsync()
    {
        var waitStartedAt = Time.realtimeSinceStartup;
        while (!_disposed && !IsSoloLevelSelectionReady())
        {
            if (Time.realtimeSinceStartup - waitStartedAt >= SoloActivationTimeoutSeconds)
            {
                Plugin.Log.Error(
                    $"[PP UI] Solo level-selection navigation did not reactivate within " +
                    $"{SoloActivationTimeoutSeconds:0.#} seconds after a pause-menu exit; " +
                    "the pending BeatLocator screen was left for the next menu scope.");
                if (_flowCoordinator.IsPostLevelFadeActive)
                {
                    _flowCoordinator.RecoverStalledPostLevelTransition();
                }
                return;
            }

            await Task.Yield();
        }

        if (_disposed)
        {
            return;
        }

        // Scene activation and the Solo screen transition are separate steps.
        // Let its normal destination settle before replacing the parent flow.
        await Task.Delay(SoloSettleDelayMilliseconds);
        if (_disposed || !IsSoloLevelSelectionReady())
        {
            if (_disposed)
            {
                return;
            }

            Plugin.Log.Error(
                "[PP UI] Solo level-selection navigation became inactive while preparing " +
                "the pause-menu exit screen.");
            if (_flowCoordinator.IsPostLevelFadeActive)
            {
                _flowCoordinator.RecoverStalledPostLevelTransition();
            }
            return;
        }

        if (!_uiState.TryTakeQuitTerminal(out var result) || result == null)
        {
            return;
        }

        await _flowCoordinator.FadeOutForPostLevelTransitionAsync();
        await _flowCoordinator.PresentPostLevelTerminalAsync(result);
        await Task.Delay(TerminalPresentationWatchdogMilliseconds);
        if (_flowCoordinator.IsPostLevelFadeActive)
        {
            Plugin.Log.Error(
                $"[PP UI] Failed/quit screen transition for run {result.RunId} " +
                "did not complete; releasing the black screen.");
            _flowCoordinator.RecoverStalledPostLevelTransition();
        }
    }

    private bool IsSoloLevelSelectionReady()
    {
        if (!_soloFreePlayFlowCoordinator.gameObject.activeInHierarchy)
        {
            return false;
        }

        var navigationField = typeof(LevelSelectionFlowCoordinator).GetField(
            "levelSelectionNavigationController",
            BindingFlags.NonPublic | BindingFlags.Instance);
        if (navigationField?.GetValue(_soloFreePlayFlowCoordinator) is not Component navigation)
        {
            return false;
        }

        return navigation.gameObject.activeInHierarchy;
    }

    public void Dispose()
    {
        _disposed = true;
        ResultsContinuePatch.Unregister(this);
        _resultsViewController.continueButtonPressedEvent -= HandleVanillaContinue;
        _uiState.ReadyChanged -= HandleReadyChanged;
    }
}
