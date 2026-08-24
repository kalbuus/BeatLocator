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

public sealed class ScoreSaberSelect : BSMLAutomaticViewController
{
    [UIComponent("exit-button")]
    private readonly Button _exitButton = null!;
    [UIComponent("exit-icon")]
    private readonly Image _exitIcon = null!;
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
    [UIComponent("played-segments")]
    private readonly RectTransform _playedSegments = null!;
    [UIComponent("played-selection-slider")]
    private readonly Image _playedSelectionSliderImage = null!;
    [UIComponent("find-button")]
    private readonly Button _findButton = null!;

    private BeatLocatorFlowCoordinator _flowCoordinator = null!;
    private RankingSearchPreferences _preferences = null!;
    private Sprite? _nineSliceSprite;
    private Sprite? _exitIconSprite;
    private int _selectedDifficulty;
    private int _selectedPlayedFilter;
    private bool _twoSaberEnabled;
    private bool _secretEnabled;
    private bool _searchInProgress;
    private SegmentSelectionSlider? _difficultySelectionSlider;
    private SegmentSelectionSlider? _playedSelectionSlider;
    private ToggleButtonVisual? _twoSaberToggleVisual;
    private ToggleButtonVisual? _secretToggleVisual;

    [UIValue("findButtonText")]
    public string FindButtonText { get; private set; } = "<b>FIND SOME MAPS!</b>";

    [UIValue("difficulties")]
    public List<string> DifficultiesList { get; set; } =
        RankingSelectViewSupport.CreateDifficulties();

    [UIValue("playedFilters")]
    public List<string> PlayedFiltersList { get; set; } = new List<string>
    {
        "Doesn't matter",
        "Played",
        "New"
    };

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
        _selectedPlayedFilter = RankingSelectViewSupport.NormalizeSelection(
            (int)preferences.ScoreSaberPlayedSelection,
            PlayedFiltersList.Count);
        _twoSaberEnabled = preferences.TwoSaberEnabled;
        _secretEnabled = preferences.SecretDifficultyEnabled;
        preferences.DifficultySelection = _selectedDifficulty;
        preferences.ScoreSaberPlayedSelection = (ScoreSaberPlayedFilter)_selectedPlayedFilter;
    }

    [UIAction("#post-parse")]
    private void PostParse()
    {
        _nineSliceSprite ??= RankingSelectViewSupport.LoadSprite(
            RankingSelectViewSupport.NineSliceResource);
        _exitIconSprite ??= RankingSelectViewSupport.LoadSprite(
            RankingSelectViewSupport.ExitIconResource);
        _difficultySelectionSliderImage.sprite = _nineSliceSprite;
        _playedSelectionSliderImage.sprite = _nineSliceSprite;

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

        _playedSelectionSlider =
            _playedSelectionSliderImage.gameObject
                .AddComponent<SegmentSelectionSlider>();
        _playedSelectionSlider.Initialize(
            _playedSegments,
            _playedSelectionSliderImage,
            PlayedFiltersList.Count,
            _selectedPlayedFilter);
        _playedSegments.GetComponent<SegmentedControl>()
            ?.SelectCellWithNumber(_selectedPlayedFilter);

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

    [UIAction("playedFilterSelected")]
    private void OnPlayedFilterSelected(object segmentedControl, int index)
    {
        _selectedPlayedFilter = RankingSelectViewSupport.NormalizeSelection(
            index,
            PlayedFiltersList.Count);
        _preferences.ScoreSaberPlayedSelection =
            (ScoreSaberPlayedFilter)_selectedPlayedFilter;
        _playedSelectionSlider?.MoveTo(_selectedPlayedFilter);
    }

    [UIAction("find-btn-action")]
    private void OnFindPressed()
    {
        SetSearchInProgress(true);
        _flowCoordinator.FindScoreSaberMapAsync(
            (ScoreSaberPlayedFilter)_selectedPlayedFilter,
            RankingSelectViewSupport.StarBuffer,
            _twoSaberEnabled,
            _secretEnabled,
            _selectedDifficulty,
            RankingSelectViewSupport.MapCount);
    }

    internal void RepeatLastSearch()
    {
        OnFindPressed();
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
        _findButton.interactable = !_searchInProgress;
        ForceMenuLayoutRebuild();
        StartCoroutine(RebuildMenuLayoutNextFrame());
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
