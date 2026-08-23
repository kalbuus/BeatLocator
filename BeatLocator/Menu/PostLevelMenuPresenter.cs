using BeatLocator.PostLevel;
using IPA.Utilities.Async;
using System;
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

    private readonly ResultsViewController _resultsViewController;
    private readonly SoloFreePlayFlowCoordinator _soloFreePlayFlowCoordinator;
    private readonly PostLevelUiState _uiState;
    private readonly BeatLocatorFlowCoordinator _flowCoordinator;

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
        _resultsViewController.continueButtonPressedEvent += HandleVanillaContinue;
        _uiState.ReadyChanged += HandleReadyChanged;
    }

    private async void HandleVanillaContinue(ResultsViewController _)
    {
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
        _flowCoordinator.PresentPostLevelLoading(runId, provider);
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
        if (_uiState.TryTakeTerminal(out var terminalResult) && terminalResult != null)
        {
            WaitForStableSoloAndPresentTerminal(terminalResult);
            return;
        }

        if (_uiState.TryTakeReady(out var result) && result != null)
        {
            _flowCoordinator.PresentPostLevelResult(result);
        }
    }

    private async void WaitForStableSoloAndPresentTerminal(
        PostLevelTerminalResult result)
    {
        var waitStartedAt = Time.realtimeSinceStartup;
        while (!_soloFreePlayFlowCoordinator.gameObject.activeInHierarchy)
        {
            if (Time.realtimeSinceStartup - waitStartedAt >= SoloActivationTimeoutSeconds)
            {
                Plugin.Log.Error(
                    $"[PP UI] Solo flow did not reactivate within " +
                    $"{SoloActivationTimeoutSeconds:0.#} seconds after " +
                    $"run {result.RunId}; failed/quit UI was canceled.");
                return;
            }

            await Task.Yield();
        }

        // Scene activation and the Solo screen transition are separate steps.
        // Let its normal destination settle before replacing the parent flow.
        await Task.Delay(SoloSettleDelayMilliseconds);
        if (!_soloFreePlayFlowCoordinator.gameObject.activeInHierarchy)
        {
            Plugin.Log.Error(
                $"[PP UI] Solo flow became inactive while preparing the " +
                $"failed/quit screen for run {result.RunId}.");
            return;
        }

        await _flowCoordinator.FadeOutForPostLevelTransitionAsync();
        _flowCoordinator.PresentPostLevelTerminal(result);
    }

    public void Dispose()
    {
        _resultsViewController.continueButtonPressedEvent -= HandleVanillaContinue;
        _uiState.ReadyChanged -= HandleReadyChanged;
    }
}
