using BeatLocator.EvaluationManagers;
using BeatLocator.Integrations;
using BeatLocator.PostLevel;
using BeatLocator.Settings;
using HMUI;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using Zenject;

namespace BeatLocator.Menu;

internal enum MapDownloadOutcome
{
    Installed,
    AlreadyInstalled,
    TimedOut,
    Failed
}

/// <summary>
/// Owns BeatLocator's menu view and returns to the game's main menu on Back.
/// </summary>
internal sealed class BeatLocatorFlowCoordinator : FlowCoordinator
{
    private const int MapSearchTimeoutMilliseconds = 45000;
    private const int MapDownloadTimeoutMilliseconds = 120000;
    private const float LaunchFadeDurationSeconds = 0.15f;
    // Beat Saber's FadeIn reveals the final BeatLocator screen.
    private const float PostLevelRevealDurationSeconds = 0.15f;
    private const int FailedSoloSettleDelayMilliseconds = 450;
    private const int FailedTerminalWatchdogMilliseconds = 3000;

    private MainFlowCoordinator _mainFlowCoordinator = null!;
    private SoloFreePlayFlowCoordinator _soloFreePlayFlowCoordinator = null!;
    private IPlatformUserModel _platformUserModel = null!;
    private SelectViewController _selectViewController = null!;
    private LazyInject<BeatLeaderSelect> _beatLeaderSelect = null!;
    private LazyInject<ScoreSaberSelect> _scoreSaberSelect = null!;
    private ViewController? _activeRankingSelect;
    private RouletteAnimationViewController _rouletteAnimationViewController = null!;
    private PpResultViewController _ppResultViewController = null!;
    private PostLevelLoadingViewController _postLevelLoadingViewController = null!;
    private PostLevelTerminalViewController _postLevelTerminalViewController = null!;
    private SimpleDialogPromptViewController _popupViewController = null!;
    private readonly Queue<PopupRequest> _popupQueue = new Queue<PopupRequest>();
    private bool _popupPresented;
    private bool _mapSearchInProgress;
    private CancellationTokenSource? _mapSearchCancellationSource;
    private bool _mapDownloadInProgress;
    private object? _fadeInOutController;
    private bool _launchFadeActive;
    private bool _postLevelFadeActive;
    private RankingProvider _rankingProvider = RankingProvider.BeatLeader;
    private PluginConfig _config = null!;
    private RoulettePlaySessionManager _playSessionManager = null!;
    private bool _postLevelPresentationInProgress;
    private bool _postLevelFlowReady;
    private bool _nextSearchFromPostLevel;
    private PostLevelDisplayResult? _pendingPostLevelResult;
    private EvaluatedDifficulty? _postLevelRetrySelection;
    private static BeatLocatorFlowCoordinator? _activeInstance;

    [Inject]
    private void Construct(
        MainFlowCoordinator mainFlowCoordinator,
        SoloFreePlayFlowCoordinator soloFreePlayFlowCoordinator,
        IPlatformUserModel platformUserModel,
        SelectViewController selectViewController,
        LazyInject<BeatLeaderSelect> beatLeaderSelect,
        LazyInject<ScoreSaberSelect> scoreSaberSelect,
        RouletteAnimationViewController rouletteAnimationViewController,
        PpResultViewController ppResultViewController,
        PostLevelLoadingViewController postLevelLoadingViewController,
        PostLevelTerminalViewController postLevelTerminalViewController,
        SimpleDialogPromptViewController popupViewController,
        PluginConfig config,
        RoulettePlaySessionManager playSessionManager)
    {
        _activeInstance = this;
        _mainFlowCoordinator = mainFlowCoordinator;
        _soloFreePlayFlowCoordinator = soloFreePlayFlowCoordinator;
        _platformUserModel = platformUserModel;
        _selectViewController = selectViewController;
        _beatLeaderSelect = beatLeaderSelect;
        _scoreSaberSelect = scoreSaberSelect;
        _rouletteAnimationViewController = rouletteAnimationViewController;
        _ppResultViewController = ppResultViewController;
        _postLevelLoadingViewController = postLevelLoadingViewController;
        _postLevelTerminalViewController = postLevelTerminalViewController;
        _popupViewController = popupViewController;
        _config = config;
        _playSessionManager = playSessionManager;

        RefreshFadeInOutController();
        if (_fadeInOutController == null)
        {
            Plugin.Log.Warn(
                "Could not find Beat Saber's menu fade controller; " +
                "the level launch transition will use the default timing.");
        }
    }

    internal void Present()
    {
        _mainFlowCoordinator.PresentFlowCoordinator(
            this,
            null,
            ViewController.AnimationDirection.Horizontal,
            false);
    }

    internal void ShowBeatLeaderSelect()
    {
        _rankingProvider = RankingProvider.BeatLeader;
        var viewController = _beatLeaderSelect.Value;
        _activeRankingSelect = viewController;
        ReplaceTopViewController(
            viewController,
            null,
            ViewController.AnimationType.In,
            ViewController.AnimationDirection.Horizontal);
    }

    internal void ShowScoreSaberSelect()
    {
        _rankingProvider = RankingProvider.ScoreSaber;
        var viewController = _scoreSaberSelect.Value;
        _activeRankingSelect = viewController;
        ReplaceTopViewController(
            viewController,
            null,
            ViewController.AnimationType.In,
            ViewController.AnimationDirection.Horizontal);
    }

    internal void ShowRankingSelect()
    {
        _postLevelFlowReady = false;
        _postLevelRetrySelection = null;
        if (_rankingProvider == RankingProvider.ScoreSaber)
        {
            ShowScoreSaberSelect();
        }
        else
        {
            ShowBeatLeaderSelect();
        }
    }

    internal void PresentPostLevelResult(PostLevelDisplayResult result)
    {
        _rankingProvider = result.Provider;
        _pendingPostLevelResult = result;
        Plugin.Log.Info(
            $"[PP UI] Post-level result is ready for run {result.RunId}: {result.Outcome}.");
        TryShowPendingPostLevelResult();
    }

    internal void PresentPostLevelLoading(long runId, RankingProvider provider)
    {
        if (_postLevelPresentationInProgress || _postLevelFlowReady)
        {
            Plugin.Log.Warn("A BeatLocator post-level screen transition is already in progress.");
            FadeInAfterPostLevelTransition();
            return;
        }

        _rankingProvider = provider;
        _postLevelLoadingViewController.SetMessage("CALCULATING PP");
        _postLevelPresentationInProgress = true;
        Plugin.Log.Info($"[PP UI] Opening loading screen for run {runId}.");
        PresentPostLevelLoadingAfterVanillaContinue();
    }

    private void PresentPostLevelLoadingAfterVanillaContinue()
    {
        try
        {
            Plugin.Log.Info("[PP UI] Dismissing the stable Solo flow.");
            _mainFlowCoordinator.DismissFlowCoordinator(
                _soloFreePlayFlowCoordinator,
                ViewController.AnimationDirection.Horizontal,
                () =>
                {
                    Plugin.Log.Info("[PP UI] Solo flow dismissed; presenting BeatLocator flow.");
                    _mainFlowCoordinator.PresentFlowCoordinator(
                        this,
                        () =>
                        {
                            Plugin.Log.Info("[PP UI] BeatLocator flow presented; activating loading view.");
                            _activeRankingSelect = null;
                            ReplaceTopViewController(
                                _postLevelLoadingViewController,
                                () =>
                                {
                                    Plugin.Log.Info("[PP UI] Loading view is active.");
                                    _postLevelPresentationInProgress = false;
                                    _postLevelFlowReady = true;
                                    FadeInAfterPostLevelTransition();
                                    TryShowPendingPostLevelResult();
                                },
                                ViewController.AnimationType.None,
                                ViewController.AnimationDirection.Horizontal);
                        },
                        ViewController.AnimationDirection.Horizontal,
                        false);
                },
                false);
        }
        catch (Exception exception)
        {
            _postLevelPresentationInProgress = false;
            FadeInAfterPostLevelTransition();
            Plugin.Log.Error($"[PP UI] Could not open the loading screen: {exception}");
        }
    }

    internal void PresentPostLevelTerminal(PostLevelTerminalResult result)
    {
        if (_postLevelPresentationInProgress || _postLevelFlowReady)
        {
            Plugin.Log.Warn("A BeatLocator post-level screen transition is already in progress.");
            FadeInAfterPostLevelTransition();
            return;
        }

        _rankingProvider = result.Provider;
        _postLevelRetrySelection = result.Selection;
        _postLevelTerminalViewController.SetLevelFailed(result.LevelFailed);
        _postLevelPresentationInProgress = true;
        Plugin.Log.Info(
            $"[PP UI] Opening {(result.LevelFailed ? "failed" : "quit")} screen " +
            $"for run {result.RunId}.");

        try
        {
            _mainFlowCoordinator.DismissFlowCoordinator(
                _soloFreePlayFlowCoordinator,
                ViewController.AnimationDirection.Horizontal,
                () =>
                {
                    Plugin.Log.Info(
                        "[PP UI] Solo flow dismissed for terminal; presenting BeatLocator flow.");
                    _mainFlowCoordinator.PresentFlowCoordinator(
                        this,
                        () =>
                        {
                            Plugin.Log.Info(
                                "[PP UI] BeatLocator flow presented; activating terminal view.");
                            _activeRankingSelect = null;
                            ReplaceTopViewController(
                                _postLevelTerminalViewController,
                                () =>
                                {
                                    Plugin.Log.Info("[PP UI] Terminal view is active; removing black.");
                                    _postLevelPresentationInProgress = false;
                                    _postLevelFlowReady = true;
                                    FadeInAfterPostLevelTransition();
                                },
                                ViewController.AnimationType.None,
                                ViewController.AnimationDirection.Horizontal);
                        },
                        ViewController.AnimationDirection.Horizontal,
                        false);
                },
                false);
        }
        catch (Exception exception)
        {
            _postLevelPresentationInProgress = false;
            FadeInAfterPostLevelTransition();
            Plugin.Log.Error($"[PP UI] Could not open the failed/quit screen: {exception}");
        }
    }

    internal Task FadeOutForPostLevelTransitionAsync()
    {
        RefreshFadeInOutController();
        if (_fadeInOutController == null) return Task.CompletedTask;

        try
        {
            ApplyPostLevelBlackInstantly();
            _postLevelFadeActive = true;
            Plugin.Log.Info(
                "[PP UI] Applied Beat Saber's instant full-screen fade to black.");
            return Task.CompletedTask;
        }
        catch (Exception exception)
        {
            _postLevelFadeActive = false;
            Plugin.Log.Error($"[PP UI] Could not fade out the menu: {exception}");
            return Task.CompletedTask;
        }
    }

    private void ApplyPostLevelBlackInstantly()
    {
        if (_fadeInOutController == null) return;

        var fadeOutInstantMethod = _fadeInOutController.GetType().GetMethod(
                "FadeOutInstant",
                BindingFlags.Public | BindingFlags.Instance,
                null,
                Type.EmptyTypes,
                null)
            ?? throw new MissingMethodException(
                _fadeInOutController.GetType().Name,
                "FadeOutInstant()");
        fadeOutInstantMethod.Invoke(_fadeInOutController, Array.Empty<object>());

        // FadeOutInstant stops any running fade coroutine. If that coroutine
        // had already marked the controller as transitioning, clear the stale
        // flag because the zero-duration method itself does not do it.
        var transitionSetter = _fadeInOutController.GetType().GetMethod(
            "set_inTransition",
            BindingFlags.NonPublic | BindingFlags.Instance);
        transitionSetter?.Invoke(_fadeInOutController, new object[] { false });
    }

    private void RefreshFadeInOutController()
    {
        if (_mainFlowCoordinator == null) return;

        var currentController = typeof(MainFlowCoordinator).GetField(
                "_fadeInOut",
                BindingFlags.NonPublic | BindingFlags.Instance)
            ?.GetValue(_mainFlowCoordinator);
        if (currentController == null ||
            ReferenceEquals(currentController, _fadeInOutController))
        {
            return;
        }

        if (_fadeInOutController != null)
        {
            Plugin.Log.Info(
                "[PP UI] Menu fade controller changed; applying black to the current scene.");
        }

        _fadeInOutController = currentController;
        if (_postLevelFadeActive)
        {
            ApplyPostLevelBlackInstantly();
        }
    }

    internal static Task FadeOutActiveForPostLevelTransitionAsync()
    {
        var instance = _activeInstance;
        if (instance != null)
        {
            return instance.FadeOutForPostLevelTransitionAsync();
        }

        Plugin.Log.Warn(
            "[PP UI] Active menu fade controller is unavailable for the post-level transition.");
        return Task.CompletedTask;
    }

    internal static void RecoverActivePostLevelTransition()
    {
        var instance = _activeInstance;
        if (instance != null)
        {
            instance.RecoverStalledPostLevelTransition();
        }
    }

    internal static void PresentPendingFailedTerminalWhenSoloReady()
    {
        var instance = _activeInstance;
        if (instance == null)
        {
            Plugin.Log.Error(
                "[PP UI] Cannot present the pending failed terminal: " +
                "the active BeatLocator flow is unavailable.");
            return;
        }

        instance.PresentPendingFailedTerminalWhenSoloReadyAsync();
    }

    private async void PresentPendingFailedTerminalWhenSoloReadyAsync()
    {
        var uiState = PostLevelUiState.Instance;
        if (uiState == null ||
            !uiState.TryTakeTerminal(out var terminal) ||
            terminal?.LevelFailed != true)
        {
            // A live PostLevelMenuPresenter may already own this terminal.
            return;
        }

        Plugin.Log.Info(
            $"[PP UI] Flow coordinator claimed pending failed terminal for run " +
            $"{terminal.RunId}.");

        // Use the same transition that already works for the completed-level
        // loading screen: keep the global layer fully black while vanilla
        // restores Solo, replace Solo with BeatLocator, then remove black only
        // from the terminal view's activation callback.
        await Task.Delay(FailedSoloSettleDelayMilliseconds);
        await FadeOutForPostLevelTransitionAsync();
        Plugin.Log.Info(
            $"[PP UI] Black screen is holding; presenting failed terminal for run " +
            $"{terminal.RunId} through the standard post-level flow.");
        PresentPostLevelTerminal(terminal);
        await Task.Delay(FailedTerminalWatchdogMilliseconds);
        if (IsPostLevelFadeActive)
        {
            Plugin.Log.Error(
                $"[PP UI] Failed terminal for run {terminal.RunId} did not finish opening.");
            RecoverStalledPostLevelTransition();
        }
    }

    internal bool IsPostLevelFadeActive => _postLevelFadeActive;
    internal static bool ShouldHoldPostLevelBlack =>
        _activeInstance?._postLevelFadeActive == true;

    internal void RecoverStalledPostLevelTransition()
    {
        _postLevelPresentationInProgress = false;
        _postLevelFlowReady = false;
        FadeInAfterPostLevelTransition();
    }

    internal void FadeInAfterPostLevelTransition()
    {
        RefreshFadeInOutController();
        if (!_postLevelFadeActive || _fadeInOutController == null) return;

        _postLevelFadeActive = false;
        try
        {
            var fadeInMethod = _fadeInOutController.GetType().GetMethod(
                "FadeIn",
                BindingFlags.Public | BindingFlags.Instance,
                null,
                new[] { typeof(float) },
                null)
                ?? throw new MissingMethodException(
                    _fadeInOutController.GetType().Name,
                    "FadeIn(float)");
            fadeInMethod.Invoke(
                _fadeInOutController,
                new object[] { PostLevelRevealDurationSeconds });
        }
        catch (Exception exception)
        {
            Plugin.Log.Error($"[PP UI] Could not fade the menu back in: {exception}");
        }
    }

    internal void ShowDebugCompletedLevel()
    {
        _rankingProvider = RankingProvider.BeatLeader;
        _postLevelFlowReady = true;
        _postLevelPresentationInProgress = true;
        _pendingPostLevelResult = null;
        _ppResultViewController.SetResult(new PostLevelDisplayResult
        {
            RunId = -1,
            Provider = RankingProvider.BeatLeader,
            Outcome = PpResolutionOutcome.UploadedNewBest,
            ScorePp = 256.42d,
            ProfileGain = 5.73d,
            LocalScore = 654321,
            LocalRank = "SS",
            LocalMaxCombo = 777,
            Detail = "Debug post-level result"
        });
        Plugin.Log.Info("[PP UI] Showing simulated completed-level result.");
        ReplaceTopViewController(
            _ppResultViewController,
            () => _postLevelPresentationInProgress = false,
            ViewController.AnimationType.In,
            ViewController.AnimationDirection.Horizontal);
    }

    private void TryShowPendingPostLevelResult()
    {
        if (!_postLevelFlowReady ||
            _postLevelPresentationInProgress ||
            _pendingPostLevelResult == null)
        {
            return;
        }

        var result = _pendingPostLevelResult;
        _pendingPostLevelResult = null;
        _ppResultViewController.SetResult(result);
        _postLevelPresentationInProgress = true;
        ReplaceTopViewController(
            _ppResultViewController,
            () => _postLevelPresentationInProgress = false,
            ViewController.AnimationType.In,
            ViewController.AnimationDirection.Horizontal);
    }

    internal void StartNextRoulette()
    {
        _postLevelFlowReady = false;
        _nextSearchFromPostLevel = true;
        _postLevelLoadingViewController.SetMessage("FINDING NEXT SONG");
        ReplaceTopViewController(
            _postLevelLoadingViewController,
            RepeatLastSearch,
            ViewController.AnimationType.In,
            ViewController.AnimationDirection.Horizontal);
    }

    internal void RetryPostLevelMap()
    {
        var selection = _postLevelRetrySelection;
        if (selection == null)
        {
            ShowPopup("RETRY UNAVAILABLE", "The previous roulette map is no longer available.");
            return;
        }

        _postLevelFlowReady = false;
        _postLevelLoadingViewController.SetMessage("LOADING LEVEL");
        ReplaceTopViewController(
            _postLevelLoadingViewController,
            () => RetryPostLevelMapAsync(selection),
            ViewController.AnimationType.In,
            ViewController.AnimationDirection.Horizontal);
    }

    private async void RetryPostLevelMapAsync(EvaluatedDifficulty selection)
    {
        if (await PlayMapAsync(selection)) return;

        ShowRankingSelect();
        ShowPopup("RETRY FAILED", "Beat Saber could not reopen the previous roulette map.");
    }

    private void RepeatLastSearch()
    {
        if (_rankingProvider == RankingProvider.ScoreSaber)
        {
            _scoreSaberSelect.Value.RepeatLastSearch();
            return;
        }

        _beatLeaderSelect.Value.RepeatLastSearch();
    }

    internal void ShowSelect()
    {
        CancelMapSearch();
        _activeRankingSelect = null;

        ReplaceTopViewController(
            _selectViewController,
            null,
            ViewController.AnimationType.In,
            ViewController.AnimationDirection.Horizontal);
    }

    private void ShowRoulette()
    {
        ReplaceTopViewController(
            _rouletteAnimationViewController,
            null,
            ViewController.AnimationType.In,
            ViewController.AnimationDirection.Horizontal);
    }

    internal void FindBeatLeaderMapAsync(
        bool played,
        float starBuffer,
        bool onlyTwoSaber,
        bool secretDifficulty,
        int mapBalance,
        int mapDifficulty,
        SongDurationFilter durationFilter,
        int count)
    {
        RunMapSearchAsync(
            RankingProvider.BeatLeader,
            secretDifficulty,
            cancellationToken => BLEvaluationManager.FindMapsAsync(
                played,
                starBuffer,
                onlyTwoSaber,
                mapBalance,
                mapDifficulty,
                durationFilter,
                count,
                _config,
                cancellationToken));
    }

    internal void FindScoreSaberMapAsync(
        ScoreSaberPlayedFilter playedFilter,
        float starBuffer,
        bool onlyTwoSaber,
        bool secretDifficulty,
        int mapDifficulty,
        SongDurationFilter durationFilter,
        int count)
    {
        RunMapSearchAsync(
            RankingProvider.ScoreSaber,
            secretDifficulty,
            cancellationToken => SSEvaluationManager.FindMapsAsync(
                _platformUserModel,
                playedFilter,
                starBuffer,
                onlyTwoSaber,
                mapDifficulty,
                durationFilter,
                count,
                _config,
                cancellationToken));
    }

    private async void RunMapSearchAsync(
        RankingProvider provider,
        bool secretDifficulty,
        Func<CancellationToken, Task<MapSearchResult>> searchAsync)
    {
        if (_mapSearchInProgress)
        {
            Plugin.Log.Warn($"A {provider.GetDisplayName()} map search is already in progress.");
            return;
        }

        _rankingProvider = provider;
        _mapSearchInProgress = true;
        var startedFromPostLevel = _nextSearchFromPostLevel;
        _nextSearchFromPostLevel = false;
        var cancellationSource = new CancellationTokenSource();
        _mapSearchCancellationSource = cancellationSource;
        var showRoulette = false;

        try
        {
            using var timeoutSource =
                new CancellationTokenSource(MapSearchTimeoutMilliseconds);
            using var linkedSource = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationSource.Token,
                timeoutSource.Token);

            var searchResult = await searchAsync(linkedSource.Token);

            if (!searchResult.IsSuccess || searchResult.SelectedDifficulty == null)
            {
                ShowPopup(
                    "SONG NOT FOUND",
                    searchResult.FailureReason ?? "No suitable song was found.");
                return;
            }

            _rouletteAnimationViewController.SetResult(
                searchResult.SelectedDifficulty,
                secretDifficulty);
            showRoulette = true;
        }
        catch (OperationCanceledException) when (cancellationSource.IsCancellationRequested)
        {
        }
        catch (OperationCanceledException)
        {
            var serviceName = provider.GetDisplayName();
            Plugin.Log.Error($"{serviceName} map search timed out after 45 seconds.");
            ShowPopup(
                "SEARCH TIMED OUT",
                $"{serviceName} did not respond within 45 seconds. Check your connection and try again.");
        }
        catch (Exception exception)
        {
            Plugin.Log.Error($"Unexpected error while finding maps: {exception}");
            ShowPopup(
                "SONG SEARCH FAILED",
                $"The search stopped because of an unexpected error: {exception.Message}");
        }
        finally
        {
            var wasCanceled = cancellationSource.IsCancellationRequested;

            if (ReferenceEquals(_mapSearchCancellationSource, cancellationSource))
            {
                _mapSearchCancellationSource = null;
                _mapSearchInProgress = false;
                if (provider == RankingProvider.ScoreSaber)
                {
                    _scoreSaberSelect.Value.SetSearchInProgress(false);
                }
                else
                {
                    _beatLeaderSelect.Value.SetSearchInProgress(false);
                }
            }

            cancellationSource.Dispose();

            if (showRoulette && !wasCanceled)
            {
                ShowRoulette();
            }
            else if (startedFromPostLevel && !wasCanceled)
            {
                ShowRankingSelect();
            }
        }
    }

    private void CancelMapSearch()
    {
        _mapSearchCancellationSource?.Cancel();
    }

    internal void ShowPopup(
        string title,
        string message,
        string buttonText = "OK")
    {
        _popupQueue.Enqueue(new PopupRequest(title, message, buttonText));
        PresentNextPopup();
    }

    private void PresentNextPopup()
    {
        if (_popupPresented || _popupQueue.Count == 0) return;

        var popup = _popupQueue.Dequeue();
        _popupPresented = true;
        _popupViewController.Init(
            popup.Title,
            popup.Message,
            popup.ButtonText,
            _ => DismissViewController(
                _popupViewController,
                ViewController.AnimationDirection.Vertical,
                () =>
                {
                    _popupPresented = false;
                    PresentNextPopup();
                }));
        PresentViewController(_popupViewController);
    }

    private readonly struct PopupRequest
    {
        internal PopupRequest(string title, string message, string buttonText)
        {
            Title = title;
            Message = message;
            ButtonText = buttonText;
        }

        internal string Title { get; }
        internal string Message { get; }
        internal string ButtonText { get; }
    }

    internal async Task<MapDownloadOutcome> DownloadMapAsync(EvaluatedDifficulty selectedDifficulty)
    {
        if (_mapDownloadInProgress)
        {
            throw new InvalidOperationException("A BeatLocator map download is already in progress.");
        }

        _mapDownloadInProgress = true;
        try
        {
            using var cancellationSource =
                new CancellationTokenSource(MapDownloadTimeoutMilliseconds);
            var downloaded = await BetterSongSearchDownloadManager.DownloadMapAsync(
                selectedDifficulty.Map,
                cancellationSource.Token);

            return downloaded
                ? MapDownloadOutcome.Installed
                : MapDownloadOutcome.AlreadyInstalled;
        }
        catch (OperationCanceledException)
        {
            Plugin.Log.Error("BetterSongSearch map download timed out after 120 seconds.");
            return MapDownloadOutcome.TimedOut;
        }
        catch (Exception exception)
        {
            Plugin.Log.Error($"BetterSongSearch could not download the selected map: {exception}");
            return MapDownloadOutcome.Failed;
        }
        finally
        {
            _mapDownloadInProgress = false;
        }
    }

    internal bool IsMapInstalled(EvaluatedDifficulty selectedDifficulty)
    {
        try
        {
            return BetterSongSearchDownloadManager.IsMapInstalled(selectedDifficulty.Map);
        }
        catch (Exception exception)
        {
            Plugin.Log.Warn(
                $"Could not check whether the roulette map is already installed: {exception.Message}");
            return false;
        }
    }

    internal async Task<bool> PlayMapAsync(EvaluatedDifficulty selectedDifficulty)
    {
        try
        {
            var resolvedLevel = await BeatSaberLevelLauncher.ResolveAsync(selectedDifficulty);
            var key = resolvedLevel.Key;
            var state = new LevelSelectionFlowCoordinator.State(
                SelectLevelCategoryViewController.LevelCategory.All,
                SongCore.Loader.CustomLevelsPack,
                in key,
                resolvedLevel.Level);

            await FadeOutBeforeSoloFlowAsync(resolvedLevel);
            _soloFreePlayFlowCoordinator.Setup(state);
            StopRouletteAndPresentSoloFlow(resolvedLevel, selectedDifficulty);
            return true;
        }
        catch (Exception exception)
        {
            FadeBackInAfterLaunchFailure();
            Plugin.Log.Error($"Could not start the selected Beat Saber level: {exception}");
            return false;
        }
    }

    private void StopRouletteAndPresentSoloFlow(
        BeatSaberLevelLauncher.ResolvedLevel resolvedLevel,
        EvaluatedDifficulty selectedDifficulty)
    {
        _postLevelFlowReady = false;
        _postLevelPresentationInProgress = false;
        _pendingPostLevelResult = null;
        _postLevelRetrySelection = null;
        _mainFlowCoordinator.DismissFlowCoordinator(
            this,
            ViewController.AnimationDirection.Horizontal,
            () => _mainFlowCoordinator.PresentFlowCoordinator(
                _soloFreePlayFlowCoordinator,
                () => SelectDifficultyAndPressPlayAsync(resolvedLevel, selectedDifficulty),
                ViewController.AnimationDirection.Horizontal,
                false));
    }

    private async void SelectDifficultyAndPressPlayAsync(
        BeatSaberLevelLauncher.ResolvedLevel resolvedLevel,
        EvaluatedDifficulty selectedDifficulty)
    {
        var sessionArmed = false;
        try
        {
            await BeatSaberLevelLauncher.SelectDifficultyAsync(
                _soloFreePlayFlowCoordinator,
                resolvedLevel);
            _playSessionManager.Begin(
                _rankingProvider,
                selectedDifficulty,
                resolvedLevel.Key);
            sessionArmed = true;
            PressSoloPlayButton();
            _launchFadeActive = false;
        }
        catch (Exception exception)
        {
            if (sessionArmed)
            {
                _playSessionManager.CancelCurrent();
            }
            FadeBackInAfterLaunchFailure();
            Plugin.Log.Error($"Could not select the requested difficulty: {exception}");
        }
    }

    private void PressSoloPlayButton()
    {
        var playMethod = typeof(SinglePlayerLevelSelectionFlowCoordinator).GetMethod(
            "ActionButtonWasPressed",
            BindingFlags.NonPublic | BindingFlags.Instance)
            ?? throw new MissingMethodException(
                nameof(SinglePlayerLevelSelectionFlowCoordinator),
                "ActionButtonWasPressed");

        // Use the same handler as the real Play button so 90/360 degree prompts
        // and the player's current modifiers/settings behave normally.
        playMethod.Invoke(_soloFreePlayFlowCoordinator, null);
    }

    private Task FadeOutBeforeSoloFlowAsync(
        BeatSaberLevelLauncher.ResolvedLevel resolvedLevel)
    {
        if (_fadeInOutController == null || RequiresMovementPrompt(resolvedLevel.Key))
        {
            return Task.CompletedTask;
        }

        var fadeOutMethod = _fadeInOutController.GetType().GetMethod(
            "FadeOut",
            BindingFlags.Public | BindingFlags.Instance,
            null,
            new[] { typeof(float), typeof(Action) },
            null)
            ?? throw new MissingMethodException(
                _fadeInOutController.GetType().Name,
                "FadeOut(float, Action)");
        var completionSource = new TaskCompletionSource<bool>();
        _launchFadeActive = true;
        fadeOutMethod.Invoke(
            _fadeInOutController,
            new object[]
            {
                LaunchFadeDurationSeconds,
                new Action(() => completionSource.TrySetResult(true))
            });
        return completionSource.Task;
    }

    private void FadeBackInAfterLaunchFailure()
    {
        if (!_launchFadeActive || _fadeInOutController == null) return;

        _launchFadeActive = false;
        try
        {
            var fadeInMethod = _fadeInOutController.GetType().GetMethod(
                "FadeIn",
                BindingFlags.Public | BindingFlags.Instance,
                null,
                new[] { typeof(float) },
                null)
                ?? throw new MissingMethodException(
                    _fadeInOutController.GetType().Name,
                    "FadeIn(float)");
            fadeInMethod.Invoke(
                _fadeInOutController,
                new object[] { LaunchFadeDurationSeconds });
        }
        catch (Exception exception)
        {
            Plugin.Log.Error($"Could not restore the menu fade after launch failure: {exception}");
        }
    }

    private static bool RequiresMovementPrompt(BeatmapKey key)
    {
        var characteristic = key.beatmapCharacteristic?.serializedName ?? string.Empty;
        return characteristic.StartsWith("90", StringComparison.OrdinalIgnoreCase) ||
               characteristic.StartsWith("360", StringComparison.OrdinalIgnoreCase);
    }

    internal void Exit()
    {
        _mainFlowCoordinator.DismissFlowCoordinator(
            this,
            ViewController.AnimationDirection.Horizontal,
            null);
    }

    protected override void DidActivate(
        bool firstActivation,
        bool addedToHierarchy,
        bool screenSystemEnabling)
    {
        if (firstActivation)
        {
            ProvideInitialViewControllers(_selectViewController);
        }
    }

    protected override void BackButtonWasPressed(ViewController topViewController)
    {
        if (_activeRankingSelect != null &&
            topViewController == _activeRankingSelect)
        {
            ShowSelect();
            return;
        }

        if (topViewController == _rouletteAnimationViewController)
        {
            if (!_mapDownloadInProgress)
            {
                ShowRankingSelect();
            }
            return;
        }

        if (topViewController == _ppResultViewController)
        {
            ShowRankingSelect();
            return;
        }

        if (topViewController == _postLevelLoadingViewController)
        {
            if (!_mapSearchInProgress)
            {
                ShowRankingSelect();
            }
            return;
        }

        if (topViewController == _postLevelTerminalViewController)
        {
            ShowRankingSelect();
            return;
        }

        Exit();
    }
}
