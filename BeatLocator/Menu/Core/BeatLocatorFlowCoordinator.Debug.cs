#if BEATLOCATOR_DEBUG_UI
using HMUI;
using BeatLocator.EvaluationManagers;
using BeatLocator.PostLevel;
using Zenject;
#endif

namespace BeatLocator.Menu;

internal sealed partial class BeatLocatorFlowCoordinator
{
    private bool ReturnFromDebugPreview()
    {
#if BEATLOCATOR_DEBUG_UI
        if (!_debugPreviewActive) return false;
        ShowDebugMenu();
        return true;
#else
        return false;
#endif
    }

#if BEATLOCATOR_DEBUG_UI
    [Inject] private LazyInject<DebugUiViewController> _debugUiViewController = null!;
    private bool _debugPreviewActive;

    internal void ShowDebugMenu()
    {
        if (isInTransition || _mainFlowSwitchInProgress) return;
        _debugPreviewActive = false;
        CancelPendingOperations();
        HideGameplaySettings();
        ReplaceTopViewController(_debugUiViewController.Value, null,
            ViewController.AnimationType.In, ViewController.AnimationDirection.Horizontal);
    }

    internal void ShowDebugScreen(DebugUiScreen screen)
    {
        if (isInTransition || _mainFlowSwitchInProgress) return;
        CancelPendingOperations();
        HideGameplaySettings();
        _debugPreviewActive = true;
        ViewController view;
        switch (screen)
        {
            case DebugUiScreen.Failed:
            case DebugUiScreen.Quit:
                var terminal = _postLevelTerminalViewController.Value;
                terminal.SetLevelFailed(screen == DebugUiScreen.Failed);
                view = terminal;
                break;
            case DebugUiScreen.LoadingPp:
            case DebugUiScreen.LoadingSong:
                var loading = _postLevelLoadingViewController.Value;
                loading.SetMessage(screen == DebugUiScreen.LoadingPp ? "CALCULATING PP" : "FINDING NEXT SONG");
                view = loading;
                break;
            case DebugUiScreen.Roulette:
                var roulette = _rouletteAnimationViewController.Value;
                roulette.SetResult(new EvaluatedDifficulty(new RecommendationMap
                {
                    Name = "Debug song", SubName = "UI preview", Author = "Sample artist", Mapper = "Sample mapper"
                }, new RecommendationDifficulty
                {
                    DifficultyName = "ExpertPlus", ModeName = "Standard", Stars = 8.21f,
                    PassRating = 6f, TechRating = 6f, AccRating = 7f
                }, 1f), false);
                view = roulette;
                break;
            default:
                var result = _ppResultViewController.Value;
                result.SetResult(new PostLevelDisplayResult
                {
                    RunId = -1, Provider = RankingProvider.BeatLeader,
                    Outcome = screen == DebugUiScreen.NonPb ? PpResolutionOutcome.NonPersonalBest
                        : screen == DebugUiScreen.UploadFailed ? PpResolutionOutcome.UploadFailed
                        : PpResolutionOutcome.UploadedNewBest,
                    ScorePp = 256.42d, ProfileGain = 5.73d, LocalScore = 654321,
                    LocalRank = "SS", LocalMaxCombo = 777, Detail = "Debug UI preview"
                });
                view = result;
                break;
        }
        Plugin.Log.Info($"[Debug UI] Showing {screen} preview.");
        ReplaceTopViewController(view, null, ViewController.AnimationType.In,
            ViewController.AnimationDirection.Horizontal);
    }
#endif
}
