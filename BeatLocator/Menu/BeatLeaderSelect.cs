using System;
using System.Collections.Generic;
using System.IO;
using BeatSaberMarkupLanguage.Attributes;
using BeatSaberMarkupLanguage.Components;
using BeatSaberMarkupLanguage.ViewControllers;
using UnityEngine;
using UnityEngine.UI;
using Zenject;

namespace BeatLocator.Menu;

public class BeatLeaderSelect : BSMLAutomaticViewController
{
    private const float StarBuffer = 0.5f;
    private const int MapCount = 60;
    private const string ToggleOffResource = "BeatLocator.Assets.off.png";
    private const string ToggleOnResource = "BeatLocator.Assets.on.png";

    [UIComponent("played-toggle-image")]
    private readonly ClickableImage _playedToggleImage = null!;
    [UIComponent("two-saber-toggle-image")]
    private readonly ClickableImage _twoSaberToggleImage = null!;
    [UIComponent("secret-toggle-image")]
    private readonly ClickableImage _secretToggleImage = null!;
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

    private Sprite? _toggleOffSprite;
    private Sprite? _toggleOnSprite;
    private BeatLocatorFlowCoordinator _flowCoordinator = null!;
    private int _selectedDifficulty;
    private int _selectedBalance;
    private bool _searchInProgress;
    private SegmentSelectionSlider? _difficultySelectionSlider;
    private SegmentSelectionSlider? _balanceSelectionSlider;

    [UIValue("findButtonText")]
    public string FindButtonText { get; private set; } = "<b>FIND SOME MAPS!</b>";

    [Inject]
    private void Construct(BeatLocatorFlowCoordinator flowCoordinator)
    {
        _flowCoordinator = flowCoordinator;
    }

    [UIValue("difficulties")]
    public List<string> DifficultiesList { get; set; } = new List<string> { "Super Easy", "Easy", "Okay", "A Bit Hard", "E N D  M E" };

    [UIAction("difficultySelected")]
    private void OnDifficultySelected(object segmentedControl, int index)
    {
        _selectedDifficulty = index;
        _difficultySelectionSlider?.MoveTo(index);
        Plugin.Log.Info($"Selected difficulty index: {index}");
    }

    [UIValue("balance")]
    public List<string> BalancesList { get; set; } = new List<string> { "T  E  C  H", "TECH", "Balanced", "PASS", "P  A  S  S" };

    [UIAction("balanceSelected")]
    private void OnBalanceSelected(object segmentedControl, int index)
    {
        _selectedBalance = index;
        _balanceSelectionSlider?.MoveTo(index);
        Plugin.Log.Info($"Selected balance index: {index}");
    }

    [UIAction("#post-parse")]
    private void PostParse()
    {
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
    }

    [UIAction("find-btn-action")]
    private void OnFindPressed()
    {
        SetSearchInProgress(true);
        Plugin.Log.Info("Starting BeatLeader map search.");

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
        _playedToggleImage.sprite = _playedEnabled
            ? _toggleOnSprite ??= LoadSprite(ToggleOnResource)
            : _toggleOffSprite ??= LoadSprite(ToggleOffResource);
    }
    
    [UIAction("toggleTwoSaber")]
    private void OnToggleTwoSaber()
    {
        _twoSaberEnabled = !_twoSaberEnabled;
        _twoSaberToggleImage.sprite = _twoSaberEnabled
            ? _toggleOnSprite ??= LoadSprite(ToggleOnResource)
            : _toggleOffSprite ??= LoadSprite(ToggleOffResource);
    }
    
    [UIAction("toggleSecretDifficulty")]
    private void OnToggleSecretDifficulty()
    {
        _secretEnabled = !_secretEnabled;
        _secretToggleImage.sprite = _secretEnabled
            ? _toggleOnSprite ??= LoadSprite(ToggleOnResource)
            : _toggleOffSprite ??= LoadSprite(ToggleOffResource);
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
