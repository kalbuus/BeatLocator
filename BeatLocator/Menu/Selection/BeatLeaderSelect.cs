using System.Collections;
using System.Collections.Generic;
using BeatSaberMarkupLanguage.Attributes;
using BeatSaberMarkupLanguage.ViewControllers;
using BeatLocator.Settings;
using HMUI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Zenject;

namespace BeatLocator.Menu;

/// <summary>
/// BeatLeader-only search settings. ScoreSaber has its own controller and BSML.
/// </summary>
public sealed class BeatLeaderSelect : BSMLAutomaticViewController
{
    [UIComponent("exit-button")]
    private readonly Button _exitButton = null!;
    [UIComponent("exit-icon")]
    private readonly Image _exitIcon = null!;
    [UIComponent("played-toggle-button")]
    private readonly Button _playedToggleButton = null!;
    [UIComponent("played-toggle-label")]
    private readonly TMP_Text _playedToggleLabel = null!;
    [UIComponent("two-saber-toggle-button")]
    private readonly Button _twoSaberToggleButton = null!;
    [UIComponent("two-saber-toggle-label")]
    private readonly TMP_Text _twoSaberToggleLabel = null!;
    [UIComponent("secret-toggle-button")]
    private readonly Button _secretToggleButton = null!;
    [UIComponent("secret-toggle-label")]
    private readonly TMP_Text _secretToggleLabel = null!;
    [UIComponent("difficulty-segments")]
    private readonly RectTransform _difficultySegments = null!;
    [UIComponent("difficulty-selection-slider")]
    private readonly Image _difficultySelectionSliderImage = null!;
    [UIComponent("balance-segments")]
    private readonly RectTransform _balanceSegments = null!;
    [UIComponent("balance-selection-slider")]
    private readonly Image _balanceSelectionSliderImage = null!;
    [UIComponent("duration-segments")]
    private readonly RectTransform _durationSegments = null!;
    [UIComponent("duration-selection-slider")]
    private readonly Image _durationSelectionSliderImage = null!;
    [UIComponent("find-button")]
    private readonly Button _findButton = null!;

    private BeatLocatorFlowCoordinator _flowCoordinator = null!;
    private RankingSearchPreferences _preferences = null!;
    private Sprite? _nineSliceSprite;
    private Sprite? _exitIconSprite;
    private int _selectedDifficulty;
    private int _selectedBalance;
    private int _selectedDuration;
    private bool _playedEnabled;
    private bool _twoSaberEnabled;
    private bool _secretEnabled;
    private bool _searchInProgress;
    private SegmentSelectionSlider? _difficultySelectionSlider;
    private SegmentSelectionSlider? _balanceSelectionSlider;
    private SegmentSelectionSlider? _durationSelectionSlider;
    private ToggleButtonVisual? _playedToggleVisual;
    private ToggleButtonVisual? _twoSaberToggleVisual;
    private ToggleButtonVisual? _secretToggleVisual;

    [UIValue("findButtonText")]
    public string FindButtonText { get; private set; } = "<b>FIND SOME MAPS!</b>";

    [UIValue("difficulties")]
    public List<string> DifficultiesList { get; set; } =
        RankingSelectViewSupport.CreateDifficulties();

    [UIValue("balance")]
    public List<string> BalancesList { get; set; } = new List<string>
    {
        "T  E  C  H",
        "TECH",
        "Balanced",
        "PASS",
        "P  A  S  S"
    };

    [UIValue("durations")]
    public List<string> DurationsList { get; set; } =
        RankingSelectViewSupport.CreateDurations();

    [Inject]
    private void Construct(
        BeatLocatorFlowCoordinator flowCoordinator,
        RankingSearchPreferences preferences)
    {
        _flowCoordinator = flowCoordinator;
        _preferences = preferences;
        _selectedDifficulty = RankingSelectViewSupport.NormalizeSelection(
            preferences.DifficultySelection,
            DifficultiesList.Count);
        _selectedBalance = RankingSelectViewSupport.NormalizeSelection(
            preferences.BalanceSelection,
            BalancesList.Count);
        _selectedDuration = RankingSelectViewSupport.NormalizeSelection(
            (int)preferences.DurationSelection,
            DurationsList.Count);
        _playedEnabled = preferences.PlayedEnabled;
        _twoSaberEnabled = preferences.TwoSaberEnabled;
        _secretEnabled = preferences.SecretDifficultyEnabled;

        preferences.DifficultySelection = _selectedDifficulty;
        preferences.BalanceSelection = _selectedBalance;
        preferences.DurationSelection = (SongDurationFilter)_selectedDuration;
    }

    [UIAction("#post-parse")]
    private void PostParse()
    {
        _nineSliceSprite ??= RankingSelectViewSupport.LoadSprite(
            RankingSelectViewSupport.NineSliceResource);
        _exitIconSprite ??= RankingSelectViewSupport.LoadSprite(
            RankingSelectViewSupport.ExitIconResource);
        _difficultySelectionSliderImage.sprite = _nineSliceSprite;
        _balanceSelectionSliderImage.sprite = _nineSliceSprite;
        _durationSelectionSliderImage.sprite = _nineSliceSprite;

        var exitVisual = _exitButton.gameObject.AddComponent<ExitButtonVisual>();
        exitVisual.Initialize(
            _exitButton,
            _exitIcon,
            _nineSliceSprite,
            _exitIconSprite);

        _difficultySelectionSlider =
            _difficultySelectionSliderImage.gameObject
                .AddComponent<SegmentSelectionSlider>();
        _difficultySelectionSlider.Initialize(
            _difficultySegments,
            _difficultySelectionSliderImage,
            DifficultiesList.Count,
            _selectedDifficulty);
        _difficultySegments.GetComponent<SegmentedControl>()
            ?.SelectCellWithNumber(_selectedDifficulty);

        _balanceSelectionSlider =
            _balanceSelectionSliderImage.gameObject
                .AddComponent<SegmentSelectionSlider>();
        _balanceSelectionSlider.Initialize(
            _balanceSegments,
            _balanceSelectionSliderImage,
            BalancesList.Count,
            _selectedBalance);
        _balanceSegments.GetComponent<SegmentedControl>()
            ?.SelectCellWithNumber(_selectedBalance);

        _durationSelectionSlider =
            _durationSelectionSliderImage.gameObject
                .AddComponent<SegmentSelectionSlider>();
        _durationSelectionSlider.Initialize(
            _durationSegments,
            _durationSelectionSliderImage,
            DurationsList.Count,
            _selectedDuration);
        _durationSegments.GetComponent<SegmentedControl>()
            ?.SelectCellWithNumber(_selectedDuration);

        _playedToggleVisual = CreateToggleVisual(
            _playedToggleButton,
            _playedToggleLabel,
            _playedEnabled);
        _twoSaberToggleVisual = CreateToggleVisual(
            _twoSaberToggleButton,
            _twoSaberToggleLabel,
            _twoSaberEnabled);
        _secretToggleVisual = CreateToggleVisual(
            _secretToggleButton,
            _secretToggleLabel,
            _secretEnabled);
    }

    [UIAction("difficultySelected")]
    private void OnDifficultySelected(object segmentedControl, int index)
    {
        _selectedDifficulty = RankingSelectViewSupport.NormalizeSelection(
            index,
            DifficultiesList.Count);
        _preferences.DifficultySelection = _selectedDifficulty;
        _difficultySelectionSlider?.MoveTo(_selectedDifficulty);
    }

    [UIAction("balanceSelected")]
    private void OnBalanceSelected(object segmentedControl, int index)
    {
        _selectedBalance = RankingSelectViewSupport.NormalizeSelection(
            index,
            BalancesList.Count);
        _preferences.BalanceSelection = _selectedBalance;
        _balanceSelectionSlider?.MoveTo(_selectedBalance);
    }

    [UIAction("durationSelected")]
    private void OnDurationSelected(object segmentedControl, int index)
    {
        _selectedDuration = RankingSelectViewSupport.NormalizeSelection(
            index,
            DurationsList.Count);
        _preferences.DurationSelection = (SongDurationFilter)_selectedDuration;
        _durationSelectionSlider?.MoveTo(_selectedDuration);
    }

    [UIAction("find-btn-action")]
    private void OnFindPressed()
    {
        SetSearchInProgress(true);
        _flowCoordinator.FindBeatLeaderMapAsync(
            _playedEnabled,
            RankingSelectViewSupport.StarBuffer,
            _twoSaberEnabled,
            _secretEnabled,
            _selectedBalance,
            _selectedDifficulty,
            (SongDurationFilter)_selectedDuration,
            RankingSelectViewSupport.MapCount);
    }

    internal void RepeatLastSearch()
    {
        OnFindPressed();
    }

    [UIAction("togglePlayed")]
    private void OnTogglePlayed()
    {
        _playedEnabled = !_playedEnabled;
        _preferences.PlayedEnabled = _playedEnabled;
        _playedToggleVisual?.SetActive(_playedEnabled);
    }

    [UIAction("toggleTwoSaber")]
    private void OnToggleTwoSaber()
    {
        _twoSaberEnabled = !_twoSaberEnabled;
        _preferences.TwoSaberEnabled = _twoSaberEnabled;
        _twoSaberToggleVisual?.SetActive(_twoSaberEnabled);
    }

    [UIAction("toggleSecretDifficulty")]
    private void OnToggleSecretDifficulty()
    {
        _secretEnabled = !_secretEnabled;
        _preferences.SecretDifficultyEnabled = _secretEnabled;
        _secretToggleVisual?.SetActive(_secretEnabled);
    }

    [UIAction("exitPressed")]
    private void OnExitPressed()
    {
        _flowCoordinator.ShowSelect();
    }

    protected override void DidActivate(
        bool firstActivation,
        bool addedToHierarchy,
        bool screenSystemEnabling)
    {
        base.DidActivate(firstActivation, addedToHierarchy, screenSystemEnabling);
        SyncDurationSelection();
        _findButton.interactable = !_searchInProgress;
        ForceMenuLayoutRebuild();
        StartCoroutine(RebuildMenuLayoutNextFrame());
    }

    private void SyncDurationSelection()
    {
        var selectedDuration = RankingSelectViewSupport.NormalizeSelection(
            (int)_preferences.DurationSelection,
            DurationsList.Count);
        if (_selectedDuration == selectedDuration) return;

        _selectedDuration = selectedDuration;
        _durationSegments.GetComponent<SegmentedControl>()
            ?.SelectCellWithNumber(_selectedDuration);
        _durationSelectionSlider?.MoveTo(_selectedDuration);
    }

    internal void SetSearchInProgress(bool searchInProgress)
    {
        _searchInProgress = searchInProgress;
        FindButtonText = searchInProgress
            ? "<b>LOADING...</b>"
            : "<b>FIND SOME MAPS!</b>";
        NotifyPropertyChanged(nameof(FindButtonText));
        if (_findButton != null)
        {
            _findButton.interactable = !searchInProgress;
        }
    }

    private ToggleButtonVisual CreateToggleVisual(
        Button button,
        TMP_Text label,
        bool active)
    {
        return RankingSelectViewSupport.InitializeToggleVisual(
            button,
            label,
            active,
            _nineSliceSprite!,
            _difficultySelectionSliderImage);
    }

    private IEnumerator RebuildMenuLayoutNextFrame()
    {
        yield return null;
        ForceMenuLayoutRebuild();
    }

    private void ForceMenuLayoutRebuild()
    {
        if (transform is RectTransform viewRoot)
        {
            RankingSelectViewSupport.ForceLayout(viewRoot);
        }
    }
}
