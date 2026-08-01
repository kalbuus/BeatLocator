using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using BeatSaberMarkupLanguage.Attributes;
using BeatSaberMarkupLanguage.ViewControllers;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Zenject;

namespace BeatLocator.Menu;

public class BeatLeaderSelect : BSMLAutomaticViewController
{
    private const float StarBuffer = 0.5f;
    private const int MapCount = 60;
    private const string NineSliceResource =
        "BeatLocator.Assets.9slice_bg.png";
    private const string ExitIconResource =
        "BeatLocator.Assets.x_mark.png";

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
    [UIComponent("find-button")]
    private readonly Button _findButton = null!;

    private Sprite? _nineSliceSprite;
    private Sprite? _exitIconSprite;
    private BeatLocatorFlowCoordinator _flowCoordinator = null!;
    private PluginConfig _config = null!;
    private int _selectedDifficulty;
    private int _selectedBalance;
    private bool _searchInProgress;
    private SegmentSelectionSlider? _difficultySelectionSlider;
    private SegmentSelectionSlider? _balanceSelectionSlider;
    private ToggleButtonVisual? _playedToggleVisual;
    private ToggleButtonVisual? _twoSaberToggleVisual;
    private ToggleButtonVisual? _secretToggleVisual;

    [UIValue("findButtonText")]
    public string FindButtonText { get; private set; } = "<b>FIND SOME MAPS!</b>";

    [Inject]
    private void Construct(
        BeatLocatorFlowCoordinator flowCoordinator,
        PluginConfig config)
    {
        _flowCoordinator = flowCoordinator;
        _config = config;
        _selectedDifficulty = NormalizeSelection(
            config.BeatLeaderDifficultySelection,
            DifficultiesList.Count);
        _selectedBalance = NormalizeSelection(
            config.BeatLeaderBalanceSelection,
            BalancesList.Count);
        _playedEnabled = config.BeatLeaderPlayedEnabled;
        _twoSaberEnabled = config.BeatLeaderTwoSaberEnabled;
        _secretEnabled = config.BeatLeaderSecretDifficultyEnabled;

        // Persist normalized values if an older or manually edited config
        // contains an index outside the available segment range.
        config.BeatLeaderDifficultySelection = _selectedDifficulty;
        config.BeatLeaderBalanceSelection = _selectedBalance;
    }

    [UIValue("difficulties")]
    public List<string> DifficultiesList { get; set; } = new List<string> { "Super Easy", "Easy", "Okay", "A Bit Hard", "E N D  M E" };

    [UIAction("difficultySelected")]
    private void OnDifficultySelected(object segmentedControl, int index)
    {
        _selectedDifficulty = NormalizeSelection(index, DifficultiesList.Count);
        _config.BeatLeaderDifficultySelection = _selectedDifficulty;
        _difficultySelectionSlider?.MoveTo(_selectedDifficulty);
    }

    [UIValue("balance")]
    public List<string> BalancesList { get; set; } = new List<string> { "T  E  C  H", "TECH", "Balanced", "PASS", "P  A  S  S" };

    [UIAction("balanceSelected")]
    private void OnBalanceSelected(object segmentedControl, int index)
    {
        _selectedBalance = NormalizeSelection(index, BalancesList.Count);
        _config.BeatLeaderBalanceSelection = _selectedBalance;
        _balanceSelectionSlider?.MoveTo(_selectedBalance);
    }

    [UIAction("#post-parse")]
    private void PostParse()
    {
        _nineSliceSprite ??= LoadSprite(NineSliceResource);
        _exitIconSprite ??= LoadSprite(ExitIconResource);
        _difficultySelectionSliderImage.sprite = _nineSliceSprite;
        _balanceSelectionSliderImage.sprite = _nineSliceSprite;

        var exitVisual = _exitButton.gameObject
            .AddComponent<ExitButtonVisual>();
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

        _balanceSelectionSlider =
            _balanceSelectionSliderImage.gameObject
                .AddComponent<SegmentSelectionSlider>();
        _balanceSelectionSlider.Initialize(
            _balanceSegments,
            _balanceSelectionSliderImage,
            BalancesList.Count,
            _selectedBalance);

        _playedToggleVisual = InitializeToggleVisual(
            _playedToggleButton,
            _playedToggleLabel,
            _playedEnabled);
        _twoSaberToggleVisual = InitializeToggleVisual(
            _twoSaberToggleButton,
            _twoSaberToggleLabel,
            _twoSaberEnabled);
        _secretToggleVisual = InitializeToggleVisual(
            _secretToggleButton,
            _secretToggleLabel,
            _secretEnabled);
    }

    [UIAction("find-btn-action")]
    private void OnFindPressed()
    {
        SetSearchInProgress(true);

        _flowCoordinator.FindMapAsync(
            _playedEnabled,
            StarBuffer,
            _twoSaberEnabled,
            _secretEnabled,
            _selectedBalance,
            _selectedDifficulty,
            MapCount);
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

    private IEnumerator RebuildMenuLayoutNextFrame()
    {
        yield return null;
        ForceMenuLayoutRebuild();
    }

    private void ForceMenuLayoutRebuild()
    {
        var viewRoot = transform as RectTransform;
        if (!viewRoot)
        {
            return;
        }

        Canvas.ForceUpdateCanvases();
        LayoutRebuilder.ForceRebuildLayoutImmediate(viewRoot);
        Canvas.ForceUpdateCanvases();
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

    [UIAction("exitPressed")]
    private void OnExitPressed()
    {
        _flowCoordinator.ShowSelect();
    }

    private bool _playedEnabled;
    private bool _twoSaberEnabled;
    private bool _secretEnabled;
        
    [UIAction("togglePlayed")]
    private void OnTogglePlayed()
    {
        _playedEnabled = !_playedEnabled;
        _config.BeatLeaderPlayedEnabled = _playedEnabled;
        _playedToggleVisual?.SetActive(_playedEnabled);
    }
    
    [UIAction("toggleTwoSaber")]
    private void OnToggleTwoSaber()
    {
        _twoSaberEnabled = !_twoSaberEnabled;
        _config.BeatLeaderTwoSaberEnabled = _twoSaberEnabled;
        _twoSaberToggleVisual?.SetActive(_twoSaberEnabled);
    }
    
    [UIAction("toggleSecretDifficulty")]
    private void OnToggleSecretDifficulty()
    {
        _secretEnabled = !_secretEnabled;
        _config.BeatLeaderSecretDifficultyEnabled = _secretEnabled;
        _secretToggleVisual?.SetActive(_secretEnabled);
    }

    private static int NormalizeSelection(int selection, int optionCount)
    {
        return Mathf.Clamp(selection, 0, Mathf.Max(0, optionCount - 1));
    }

    private ToggleButtonVisual InitializeToggleVisual(
        Button button,
        TMP_Text label,
        bool active)
    {
        var visual = button.gameObject
            .AddComponent<ToggleButtonVisual>();
        visual.Initialize(
            button,
            label,
            _nineSliceSprite!,
            _difficultySelectionSliderImage,
            active);
        return visual;
    }

    private static Sprite LoadSprite(string resourceName)
    {
        using var stream = typeof(SelectViewController).Assembly.GetManifestResourceStream(resourceName)
                           ?? throw new InvalidOperationException($"Embedded image '{resourceName}' was not found.");
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);

        var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        if (!texture.LoadImage(buffer.ToArray()))
        {
            UnityEngine.Object.Destroy(texture);
            throw new InvalidOperationException($"Could not load image '{resourceName}'.");
        }

        return Sprite.Create(
            texture,
            new Rect(0, 0, texture.width, texture.height),
            new Vector2(0.5f, 0.5f),
            100f);
    }
}
