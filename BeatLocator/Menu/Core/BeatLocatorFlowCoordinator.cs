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
    private const int MainFlowStabilityTimeoutMilliseconds = 5000;
    private const int MainFlowSwitchTimeoutMilliseconds = 10000;

    private MainFlowCoordinator _mainFlowCoordinator = null!;
    private SoloFreePlayFlowCoordinator _soloFreePlayFlowCoordinator = null!;
    private IPlatformUserModel _platformUserModel = null!;
    private LazyInject<SelectViewController> _selectViewController = null!;
    private LazyInject<BeatLeaderSelect> _beatLeaderSelect = null!;
    private LazyInject<ScoreSaberSelect> _scoreSaberSelect = null!;
    private ViewController? _activeRankingSelect;
    private LazyInject<RouletteAnimationViewController> _rouletteAnimationViewController = null!;
    private LazyInject<PpResultViewController> _ppResultViewController = null!;
    private LazyInject<PostLevelLoadingViewController> _postLevelLoadingViewController = null!;
    private LazyInject<PostLevelTerminalViewController> _postLevelTerminalViewController = null!;
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
    private bool _mainFlowSwitchInProgress;
    private int _mainFlowSwitchGeneration;
    private static BeatLocatorFlowCoordinator? _activeInstance;

    [Inject]
    private void Construct(
        MainFlowCoordinator mainFlowCoordinator,
        SoloFreePlayFlowCoordinator soloFreePlayFlowCoordinator,
        IPlatformUserModel platformUserModel,
        LazyInject<SelectViewController> selectViewController,
        LazyInject<BeatLeaderSelect> beatLeaderSelect,
        LazyInject<ScoreSaberSelect> scoreSaberSelect,
        LazyInject<RouletteAnimationViewController> rouletteAnimationViewController,
        LazyInject<PpResultViewController> ppResultViewController,
        LazyInject<PostLevelLoadingViewController> postLevelLoadingViewController,
        LazyInject<PostLevelTerminalViewController> postLevelTerminalViewController,
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
        if (ReferenceEquals(_mainFlowCoordinator.childFlowCoordinator, this))
        {
            Plugin.Log.Warn("BeatLocator flow is already presented.");
            return;
        }

        if (_mainFlowCoordinator.childFlowCoordinator != null ||
            _mainFlowCoordinator.isInTransition ||
            _mainFlowSwitchInProgress)
        {
            Plugin.Log.Warn(
                "BeatLocator flow was not presented because another main-menu flow " +
                "or transition currently owns the ScreenSystem.");
            return;
        }

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

    internal async Task<bool> PresentPostLevelLoadingAsync(
        long runId,
        RankingProvider provider)
    {
        if (_postLevelPresentationInProgress || _postLevelFlowReady)
        {
            Plugin.Log.Warn("A BeatLocator post-level screen transition is already in progress.");
            FadeInAfterPostLevelTransition();
            return false;
        }

        _rankingProvider = provider;
        var loadingViewController = _postLevelLoadingViewController.Value;
        loadingViewController.SetMessage("CALCULATING PP");
        _postLevelPresentationInProgress = true;
        Plugin.Log.Info($"[PP UI] Opening loading screen for run {runId}.");
        return await PresentPostLevelLoadingAfterVanillaContinueAsync();
    }

    private async Task<bool> PresentPostLevelLoadingAfterVanillaContinueAsync()
    {
        var loadingViewController = _postLevelLoadingViewController.Value;
        try
        {
            var switched = await SwitchMainChildFlowAsync(
                _soloFreePlayFlowCoordinator,
                this,
                () =>
                {
                    Plugin.Log.Info("[PP UI] BeatLocator flow presented; activating loading view.");
                    _activeRankingSelect = null;
                    ReplaceTopViewController(
                        loadingViewController,
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
                "post-level loading");
            if (!switched)
            {
                _postLevelPresentationInProgress = false;
                _postLevelFlowReady = false;
                FadeInAfterPostLevelTransition();
                return false;
            }
            return true;
        }
        catch (Exception exception)
        {
            _postLevelPresentationInProgress = false;
            FadeInAfterPostLevelTransition();
            Plugin.Log.Error($"[PP UI] Could not open the loading screen: {exception}");
            return false;
        }
    }

    internal async Task<bool> PresentPostLevelTerminalAsync(PostLevelTerminalResult result)
    {
        if (_postLevelPresentationInProgress || _postLevelFlowReady)
        {
            Plugin.Log.Warn("A BeatLocator post-level screen transition is already in progress.");
            FadeInAfterPostLevelTransition();
            return false;
        }

        _rankingProvider = result.Provider;
        _postLevelRetrySelection = result.Selection;
        var terminalViewController = _postLevelTerminalViewController.Value;
        terminalViewController.SetLevelFailed(result.LevelFailed);
        _postLevelPresentationInProgress = true;
        Plugin.Log.Info(
            $"[PP UI] Opening {(result.LevelFailed ? "failed" : "quit")} screen " +
            $"for run {result.RunId}.");

        try
        {
            var switched = await SwitchMainChildFlowAsync(
                _soloFreePlayFlowCoordinator,
                this,
                () =>
                {
                    Plugin.Log.Info(
                        "[PP UI] BeatLocator flow presented; activating terminal view.");
                    _activeRankingSelect = null;
                    ReplaceTopViewController(
                        terminalViewController,
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
                "post-level terminal");
            if (!switched)
            {
                _postLevelPresentationInProgress = false;
                _postLevelFlowReady = false;
                FadeInAfterPostLevelTransition();
                return false;
            }
            return true;
        }
        catch (Exception exception)
        {
            _postLevelPresentationInProgress = false;
            FadeInAfterPostLevelTransition();
            Plugin.Log.Error($"[PP UI] Could not open the failed/quit screen: {exception}");
            return false;
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
        await PresentPostLevelTerminalAsync(terminal);
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
        var resultViewController = _ppResultViewController.Value;
        resultViewController.SetResult(new PostLevelDisplayResult
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
            resultViewController,
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
        var resultViewController = _ppResultViewController.Value;
        resultViewController.SetResult(result);
        _postLevelPresentationInProgress = true;
        ReplaceTopViewController(
            resultViewController,
            () => _postLevelPresentationInProgress = false,
            ViewController.AnimationType.In,
            ViewController.AnimationDirection.Horizontal);
    }

    internal void StartNextRoulette()
    {
        _postLevelFlowReady = false;
        _nextSearchFromPostLevel = true;
        var loadingViewController = _postLevelLoadingViewController.Value;
        loadingViewController.SetMessage("FINDING NEXT SONG");
        ReplaceTopViewController(
            loadingViewController,
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
        var loadingViewController = _postLevelLoadingViewController.Value;
        loadingViewController.SetMessage("LOADING LEVEL");
        ReplaceTopViewController(
            loadingViewController,
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
            _selectViewController.Value,
            null,
            ViewController.AnimationType.In,
            ViewController.AnimationDirection.Horizontal);
    }

    private void ShowRoulette()
    {
        ReplaceTopViewController(
            _rouletteAnimationViewController.Value,
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

            _rouletteAnimationViewController.Value.SetResult(
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
            var switched = await StopRouletteAndPresentSoloFlowAsync(
                resolvedLevel,
                selectedDifficulty);
            if (!switched)
            {
                FadeBackInAfterLaunchFailure();
            }
            return switched;
        }
        catch (Exception exception)
        {
            FadeBackInAfterLaunchFailure();
            Plugin.Log.Error($"Could not start the selected Beat Saber level: {exception}");
            return false;
        }
    }

    private Task<bool> StopRouletteAndPresentSoloFlowAsync(
        BeatSaberLevelLauncher.ResolvedLevel resolvedLevel,
        EvaluatedDifficulty selectedDifficulty)
    {
        _postLevelFlowReady = false;
        _postLevelPresentationInProgress = false;
        _pendingPostLevelResult = null;
        _postLevelRetrySelection = null;
        return SwitchMainChildFlowAsync(
            this,
            _soloFreePlayFlowCoordinator,
            () => SelectDifficultyAndPressPlayAsync(resolvedLevel, selectedDifficulty),
            "roulette launch");
    }

    private async Task<bool> SwitchMainChildFlowAsync(
        FlowCoordinator expectedCurrent,
        FlowCoordinator? next,
        Action? nextPresented,
        string operation)
    {
        if (_mainFlowSwitchInProgress)
        {
            Plugin.Log.Error(
                $"[Flow] Refused {operation}: another BeatLocator main-flow switch is active.");
            return false;
        }

        _mainFlowSwitchInProgress = true;
        var generation = ++_mainFlowSwitchGeneration;
        try
        {
            var stableDeadline = DateTimeOffset.UtcNow.AddMilliseconds(
                MainFlowStabilityTimeoutMilliseconds);
            while (_mainFlowCoordinator.isInTransition || expectedCurrent.isInTransition)
            {
                if (!ReferenceEquals(
                        _mainFlowCoordinator.childFlowCoordinator,
                        expectedCurrent))
                {
                    LogUnexpectedMainChild(operation, expectedCurrent);
                    return false;
                }

                if (DateTimeOffset.UtcNow >= stableDeadline)
                {
                    Plugin.Log.Error(
                        $"[Flow] Refused {operation}: the current main flow did not " +
                        $"finish its transition within " +
                        $"{MainFlowStabilityTimeoutMilliseconds / 1000:0} seconds.");
                    return false;
                }

                await Task.Yield();
            }

            if (!ReferenceEquals(
                    _mainFlowCoordinator.childFlowCoordinator,
                    expectedCurrent) ||
                !expectedCurrent.isActivated)
            {
                LogUnexpectedMainChild(operation, expectedCurrent);
                return false;
            }

            var completionSource = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            Plugin.Log.Info(
                $"[Flow] Switching main child for {operation}: " +
                $"{expectedCurrent.GetType().Name} -> " +
                $"{next?.GetType().Name ?? "<main menu>"} (generation {generation}).");
            _mainFlowCoordinator.DismissFlowCoordinator(
                expectedCurrent,
                ViewController.AnimationDirection.Horizontal,
                () =>
                {
                    if (generation != _mainFlowSwitchGeneration)
                    {
                        completionSource.TrySetResult(false);
                        return;
                    }

                    if (_mainFlowCoordinator.childFlowCoordinator != null)
                    {
                        Plugin.Log.Error(
                            $"[Flow] Refused to finish {operation}: another flow took " +
                            "ownership after the expected child was dismissed.");
                        completionSource.TrySetResult(false);
                        return;
                    }

                    if (next == null)
                    {
                        completionSource.TrySetResult(true);
                        return;
                    }

                    try
                    {
                        _mainFlowCoordinator.PresentFlowCoordinator(
                            next,
                            () =>
                            {
                                if (generation != _mainFlowSwitchGeneration ||
                                    !ReferenceEquals(
                                        _mainFlowCoordinator.childFlowCoordinator,
                                        next))
                                {
                                    completionSource.TrySetResult(false);
                                    return;
                                }

                                try
                                {
                                    nextPresented?.Invoke();
                                    completionSource.TrySetResult(true);
                                }
                                catch (Exception exception)
                                {
                                    completionSource.TrySetException(exception);
                                }
                            },
                            ViewController.AnimationDirection.Horizontal,
                            false);
                    }
                    catch (Exception exception)
                    {
                        completionSource.TrySetException(exception);
                    }
                },
                false);

            var timeoutTask = Task.Delay(MainFlowSwitchTimeoutMilliseconds);
            if (await Task.WhenAny(completionSource.Task, timeoutTask) != completionSource.Task)
            {
                _mainFlowSwitchGeneration++;
                Plugin.Log.Error(
                    $"[Flow] {operation} did not complete within " +
                    $"{MainFlowSwitchTimeoutMilliseconds / 1000:0} seconds; " +
                    "late callbacks will be ignored.");
                return false;
            }

            return await completionSource.Task;
        }
        catch (Exception exception)
        {
            Plugin.Log.Error($"[Flow] Could not complete {operation}: {exception}");
            return false;
        }
        finally
        {
            _mainFlowSwitchInProgress = false;
        }
    }

    private void LogUnexpectedMainChild(string operation, FlowCoordinator expectedCurrent)
    {
        var actual = _mainFlowCoordinator.childFlowCoordinator;
        Plugin.Log.Error(
            $"[Flow] Refused {operation}: expected " +
            $"{expectedCurrent.GetType().Name}, actual " +
            $"{actual?.GetType().Name ?? "<none>"}, " +
            $"mainTransition={_mainFlowCoordinator.isInTransition}, " +
            $"expectedTransition={expectedCurrent.isInTransition}, " +
            $"expectedActive={expectedCurrent.isActivated}.");
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

    internal async void Exit()
    {
        await SwitchMainChildFlowAsync(
            this,
            null,
            null,
            "BeatLocator menu exit");
    }

    protected override void DidActivate(
        bool firstActivation,
        bool addedToHierarchy,
        bool screenSystemEnabling)
    {
        if (firstActivation)
        {
            ProvideInitialViewControllers(_selectViewController.Value);
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

        if (topViewController is RouletteAnimationViewController)
        {
            if (!_mapDownloadInProgress)
            {
                ShowRankingSelect();
            }
            return;
        }

        if (topViewController is PpResultViewController)
        {
            ShowRankingSelect();
            return;
        }

        if (topViewController is PostLevelLoadingViewController)
        {
            if (!_mapSearchInProgress)
            {
                ShowRankingSelect();
            }
            return;
        }

        if (topViewController is PostLevelTerminalViewController)
        {
            ShowRankingSelect();
            return;
        }

        Exit();
    }
}
