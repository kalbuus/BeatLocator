using System;
using BeatLocator.EvaluationManagers;
using BeatLocator.Dialogue;
using Zenject;

namespace BeatLocator.PostLevel;

/// <summary>
/// Passively observes Beat Saber's standard completion event. It never replaces
/// or delays the game's own results processing.
/// </summary>
internal sealed class RouletteLevelCompletionTracker : IInitializable, IDisposable
{
    private static readonly TimeSpan MaximumPpResolutionTime = TimeSpan.FromSeconds(140);
    private readonly StandardLevelScenesTransitionSetupDataSO _transitionSetup;
    private readonly RoulettePlaySessionManager _sessionManager;
    private readonly PostLevelPpResolver _ppResolver;
    private readonly PostLevelUiState _uiState;
    private readonly BotDialogueSessionState _botDialogueSessionState;

    public RouletteLevelCompletionTracker(
        StandardLevelScenesTransitionSetupDataSO transitionSetup,
        RoulettePlaySessionManager sessionManager,
        PostLevelPpResolver ppResolver,
        PostLevelUiState uiState,
        BotDialogueSessionState botDialogueSessionState)
    {
        _transitionSetup = transitionSetup;
        _sessionManager = sessionManager;
        _ppResolver = ppResolver;
        _uiState = uiState;
        _botDialogueSessionState = botDialogueSessionState;
    }

    public void Initialize()
    {
        _transitionSetup.didFinishEvent += HandleLevelFinished;
    }

    private void HandleLevelFinished(
        StandardLevelScenesTransitionSetupDataSO transition,
        LevelCompletionResults results)
    {
        if (!_sessionManager.MatchesActive(transition.beatmapKey))
        {
            _sessionManager.CancelIfActiveRunDoesNotMatch(transition.beatmapKey);
            return;
        }

        var session = _sessionManager.TryClaim(transition.beatmapKey);
        if (session == null) return;

        if (transition.practiceSettings != null)
        {
            Plugin.Log.Warn($"[PP] Run {session.RunId} ignored because practice mode is active.");
            _uiState.Abandon(session.RunId);
            _sessionManager.Release(session.RunId);
            return;
        }

        Plugin.Log.Info(
            $"[PP] Run {session.RunId} finished locally: state={results.levelEndStateType}, " +
            $"action={results.levelEndAction}, score={results.multipliedScore}, " +
            $"rank={results.rank}, maxCombo={results.maxCombo}, fullCombo={results.fullCombo}, " +
            $"invalidated={results.invalidated}.");

        if (results.levelEndAction == LevelCompletionResults.LevelEndAction.Restart)
        {
            Plugin.Log.Info($"[PP] Run {session.RunId}: restart requested; rearming the same map.");
            _sessionManager.RearmAfterRestart(session);
            return;
        }

        if (!_uiState.ConfirmCompletion(session))
        {
            Plugin.Log.Warn(
                $"[PP] Run {session.RunId} completed, but its UI state is no longer active; " +
                "leaving the vanilla results flow untouched.");
            _sessionManager.Release(session.RunId);
            return;
        }

        if (results.levelEndAction == LevelCompletionResults.LevelEndAction.Quit ||
            results.levelEndStateType == LevelCompletionResults.LevelEndStateType.Incomplete)
        {
            Plugin.Log.Info($"[PP] Run {session.RunId}: level quit; no PP lookup will be performed.");
            _botDialogueSessionState.RecordQuit();
            _uiState.CompleteTerminal(session, false);
            _sessionManager.Release(session.RunId);
            return;
        }

        if (results.levelEndStateType == LevelCompletionResults.LevelEndStateType.Failed)
        {
            Plugin.Log.Info($"[PP] Run {session.RunId}: LEVEL FAILED; no PP lookup will be performed.");
            _botDialogueSessionState.RecordFailed();
            // Results are activated while gameplay time is still stopped. Use
            // the synchronous vanilla fade so no failed-results frame renders.
            _ = Menu.BeatLocatorFlowCoordinator.FadeOutActiveForPostLevelTransitionAsync();
            _uiState.CompleteTerminal(session, true);
            _sessionManager.Release(session.RunId);
            return;
        }

        if (results.levelEndStateType != LevelCompletionResults.LevelEndStateType.Cleared)
        {
            Plugin.Log.Warn(
                $"[PP] Run {session.RunId}: unsupported completion state " +
                $"{results.levelEndStateType}/{results.levelEndAction}.");
            _uiState.Abandon(session.RunId);
            _sessionManager.Release(session.RunId);
            return;
        }

        _botDialogueSessionState.RecordCleared();

        if (results.invalidated)
        {
            LogProviderResult(
                session,
                results,
                new ProviderPpResult
                {
                    Outcome = PpResolutionOutcome.Invalidated,
                    Detail = "Beat Saber marked the result as invalidated."
                });
            _uiState.Complete(
                session,
                new ProviderPpResult
                {
                    Outcome = PpResolutionOutcome.Invalidated,
                    Detail = "Beat Saber marked the result as invalidated."
                },
                results);
            _sessionManager.Release(session.RunId);
            return;
        }

        session.CancellationSource.CancelAfter(MaximumPpResolutionTime);
        ResolveAndLogAsync(session, results);
    }

    private async void ResolveAndLogAsync(
        RoulettePlaySession session,
        LevelCompletionResults results)
    {
        try
        {
            var providerResult = await _ppResolver.ResolveAsync(session, results);
            LogProviderResult(session, results, providerResult);
            _uiState.Complete(session, providerResult, results);
        }
        catch (Exception exception)
        {
            Plugin.Log.Error($"[PP] Run {session.RunId}: unexpected PP resolver error: {exception}");
        }
        finally
        {
            _sessionManager.Release(session.RunId);
        }
    }

    private static void LogProviderResult(
        RoulettePlaySession session,
        LevelCompletionResults localResults,
        ProviderPpResult providerResult)
    {
        var service = session.Provider.GetDisplayName();
        if (providerResult.Outcome == PpResolutionOutcome.UploadedNewBest)
        {
            Plugin.Log.Info(
                $"[PP] Run {session.RunId} {service}: UPLOADED NEW BEST; " +
                $"score_pp={FormatPp(providerResult.ScorePp)}, " +
                $"profile_gain={FormatSignedPp(providerResult.ProfileGain)}. " +
                $"{providerResult.Detail}");
            return;
        }

        if (providerResult.Outcome == PpResolutionOutcome.NonPersonalBest)
        {
            Plugin.Log.Info(
                $"[PP] Run {session.RunId} {service}: PERSONAL BEST STANDS; " +
                $"attempt_score={localResults.multipliedScore}, rank={localResults.rank}, " +
                $"max_combo={localResults.maxCombo}. {providerResult.Detail}");
            return;
        }

        Plugin.Log.Warn(
            $"[PP] Run {session.RunId} {service}: outcome={providerResult.Outcome}; " +
            $"{providerResult.Detail}");
    }

    private static string FormatPp(double? value)
    {
        return value.HasValue ? value.Value.ToString("0.00") : "n/a";
    }

    private static string FormatSignedPp(double? value)
    {
        return value.HasValue ? value.Value.ToString("+0.00;-0.00;0.00") : "n/a";
    }

    public void Dispose()
    {
        _transitionSetup.didFinishEvent -= HandleLevelFinished;
        _sessionManager.CancelCurrent();
    }
}
