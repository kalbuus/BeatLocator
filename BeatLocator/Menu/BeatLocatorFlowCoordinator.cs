using BeatLocator.EvaluationManagers;
using BeatLocator.Integrations;
using HMUI;
using System;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
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

    private MainFlowCoordinator _mainFlowCoordinator = null!;
    private SoloFreePlayFlowCoordinator _soloFreePlayFlowCoordinator = null!;
    private SelectViewController _selectViewController = null!;
    private BeatLeaderSelect _beatLeaderSelect = null!;
    private RouletteAnimationViewController _rouletteAnimationViewController = null!;
    private bool _mapSearchInProgress;
    private bool _mapDownloadInProgress;
    private PluginConfig? _config;

    [Inject]
    private void Construct(
        MainFlowCoordinator mainFlowCoordinator,
        SoloFreePlayFlowCoordinator soloFreePlayFlowCoordinator,
        SelectViewController selectViewController,
        BeatLeaderSelect beatLeaderSelect,
        RouletteAnimationViewController rouletteAnimationViewController,
        PluginConfig config)
    {
        _mainFlowCoordinator = mainFlowCoordinator;
        _soloFreePlayFlowCoordinator = soloFreePlayFlowCoordinator;
        _selectViewController = selectViewController;
        _beatLeaderSelect = beatLeaderSelect;
        _rouletteAnimationViewController = rouletteAnimationViewController;
        _config = config;
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
        ReplaceTopViewController(
            _beatLeaderSelect,
            null,
            ViewController.AnimationType.In,
            ViewController.AnimationDirection.Horizontal);
    }

    internal void ShowSelect()
    {
        if (_mapSearchInProgress) return;

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

    internal async void FindMapAsync(bool played, float starBuffer, bool onlyTwoSaber,
                                    bool secretDifficulty, int mapBalance,
                                    int mapDifficulty, int count)
    {
        if (_mapSearchInProgress)
        {
            Plugin.Log.Warn("A BeatLeader map search is already in progress.");
            return;
        }

        _mapSearchInProgress = true;
        var showRoulette = false;

        try
        {
            var searchTask = BLEvaluationManager.FindMapsAsync(
                played,
                starBuffer,
                onlyTwoSaber,
                mapBalance,
                mapDifficulty,
                count,
                _config);

            var completedTask = await Task.WhenAny(
                searchTask,
                Task.Delay(MapSearchTimeoutMilliseconds));

            if (completedTask != searchTask)
            {
                Plugin.Log.Error("BeatLeader map search timed out after 45 seconds.");
                return;
            }

            await searchTask;

            var selectedDifficulty = BLEvaluationManager.SelectedDifficulty;
            if (selectedDifficulty == null)
            {
                Plugin.Log.Warn("The map search did not select a difficulty.");
                return;
            }

            _rouletteAnimationViewController.SetResult(
                selectedDifficulty,
                secretDifficulty);
            showRoulette = true;
        }
        catch (Exception exception)
        {
            Plugin.Log.Error($"Unexpected error while finding maps: {exception}");
        }
        finally
        {
            _mapSearchInProgress = false;
            _beatLeaderSelect.SetSearchInProgress(false);

            if (showRoulette)
            {
                ShowRoulette();
            }
        }
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

            _soloFreePlayFlowCoordinator.Setup(state);
            StopRouletteAndPresentSoloFlow(resolvedLevel);
            return true;
        }
        catch (Exception exception)
        {
            Plugin.Log.Error($"Could not start the selected Beat Saber level: {exception}");
            return false;
        }
    }

    private void StopRouletteAndPresentSoloFlow(
        BeatSaberLevelLauncher.ResolvedLevel resolvedLevel)
    {
        _mainFlowCoordinator.DismissFlowCoordinator(
            this,
            ViewController.AnimationDirection.Horizontal,
            () => _mainFlowCoordinator.PresentFlowCoordinator(
                _soloFreePlayFlowCoordinator,
                () => SelectDifficultyAndPressPlayAsync(resolvedLevel),
                ViewController.AnimationDirection.Horizontal,
                false));
    }

    private async void SelectDifficultyAndPressPlayAsync(
        BeatSaberLevelLauncher.ResolvedLevel resolvedLevel)
    {
        try
        {
            await BeatSaberLevelLauncher.SelectDifficultyAsync(
                _soloFreePlayFlowCoordinator,
                resolvedLevel);
            PressSoloPlayButton();
        }
        catch (Exception exception)
        {
            Plugin.Log.Error($"Could not select the requested difficulty: {exception}");
        }
    }

    private void PressSoloPlayButton()
    {
        try
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
        catch (Exception exception)
        {
            Plugin.Log.Error($"Beat Saber's Play action failed: {exception}");
        }
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
        if (topViewController == _beatLeaderSelect)
        {
            if (!_mapSearchInProgress)
            {
                ShowSelect();
            }
            return;
        }

        if (topViewController == _rouletteAnimationViewController)
        {
            if (!_mapDownloadInProgress)
            {
                ShowBeatLeaderSelect();
            }
            return;
        }

        Exit();
    }
}
