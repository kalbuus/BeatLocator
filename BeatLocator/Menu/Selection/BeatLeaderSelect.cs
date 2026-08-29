using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using BeatSaberMarkupLanguage.Attributes;
using BeatSaberMarkupLanguage.ViewControllers;
using BeatLocator.Settings;
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
    [UIComponent("beatleader-menu-root")]
    private readonly RectTransform _menuRoot = null!;
    [UIComponent("header-row")]
    private readonly RectTransform _headerRow = null!;
    [UIComponent("difficulty-row")]
    private readonly RectTransform _difficultyRow = null!;
    [UIComponent("balance-row")]
    private readonly RectTransform _balanceRow = null!;
    [UIComponent("duration-row")]
    private readonly RectTransform _durationRow = null!;
    [UIComponent("difficulty-label")]
    private readonly TMP_Text _difficultyLabel = null!;
    [UIComponent("balance-label")]
    private readonly TMP_Text _balanceLabel = null!;
    [UIComponent("duration-label")]
    private readonly TMP_Text _durationLabel = null!;
    [UIComponent("difficulty-icon")]
    private readonly Image _difficultyIcon = null!;
    [UIComponent("balance-icon")]
    private readonly Image _balanceIcon = null!;
    [UIComponent("duration-icon")]
    private readonly Image _durationIcon = null!;
    [UIComponent("exit-button")]
    private readonly Button _exitButton = null!;
    [UIComponent("exit-artwork")]
    private readonly Image _exitArtwork = null!;
    [UIComponent("difficulty-row-background")]
    private readonly Image _difficultyRowBackground = null!;
    [UIComponent("balance-row-background")]
    private readonly Image _balanceRowBackground = null!;
    [UIComponent("duration-row-background")]
    private readonly Image _durationRowBackground = null!;
    [UIComponent("difficulty-options")]
    private readonly RectTransform _difficultyOptions = null!;
    [UIComponent("balance-options")]
    private readonly RectTransform _balanceOptions = null!;
    [UIComponent("duration-options")]
    private readonly RectTransform _durationOptions = null!;

    private BeatLocatorFlowCoordinator _flowCoordinator = null!;
    private RankingSearchPreferences _preferences = null!;
    private int _selectedDifficulty;
    private int _selectedBalance;
    private int _selectedDuration;
    private bool _playedEnabled;
    private bool _twoSaberEnabled;
    private bool _secretEnabled;
    private bool _visualsInitialized;
    private SelectionOptionButtonVisual[] _difficultyVisuals =
        Array.Empty<SelectionOptionButtonVisual>();
    private SelectionOptionButtonVisual[] _balanceVisuals =
        Array.Empty<SelectionOptionButtonVisual>();
    private SelectionOptionButtonVisual[] _durationVisuals =
        Array.Empty<SelectionOptionButtonVisual>();

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
        if (_visualsInitialized)
        {
            return;
        }

        var selectedButtonSprite = RankingSelectViewSupport.LoadSprite(
            RankingSelectViewSupport.SelectedButtonResource);
        var unselectedButtonSprite = RankingSelectViewSupport.LoadSprite(
            RankingSelectViewSupport.UnselectedButtonResource);
        var rowBackgroundSprite = RankingSelectViewSupport.LoadSlicedSprite(
            RankingSelectViewSupport.SelectionRowBackgroundResource,
            new Vector4(22f, 20f, 22f, 20f),
            10f,
            1.8f);

        LayoutMenu();

        _exitArtwork.sprite = RankingSelectViewSupport.LoadSprite(
            RankingSelectViewSupport.CrossButtonResource);
        _exitArtwork.preserveAspect = true;
        StaticSpriteButtonVisual.Initialize(_exitButton, _exitArtwork);

        InitializeRowBackground(
            _difficultyRowBackground,
            rowBackgroundSprite);
        InitializeRowBackground(
            _balanceRowBackground,
            rowBackgroundSprite);
        InitializeRowBackground(
            _durationRowBackground,
            rowBackgroundSprite);

        _difficultyVisuals = InitializeOptionGroup(
            _difficultyOptions,
            _selectedDifficulty,
            OnDifficultySelected,
            unselectedButtonSprite,
            selectedButtonSprite);
        _balanceVisuals = InitializeOptionGroup(
            _balanceOptions,
            _selectedBalance,
            OnBalanceSelected,
            unselectedButtonSprite,
            selectedButtonSprite);
        _durationVisuals = InitializeOptionGroup(
            _durationOptions,
            _selectedDuration,
            OnDurationSelected,
            unselectedButtonSprite,
            selectedButtonSprite);

        _visualsInitialized = true;
    }

    private void OnDifficultySelected(int index)
    {
        _selectedDifficulty = RankingSelectViewSupport.NormalizeSelection(
            index,
            DifficultiesList.Count);
        _preferences.DifficultySelection = _selectedDifficulty;
        ApplySelection(_difficultyVisuals, _selectedDifficulty);
    }

    private void OnBalanceSelected(int index)
    {
        _selectedBalance = RankingSelectViewSupport.NormalizeSelection(
            index,
            BalancesList.Count);
        _preferences.BalanceSelection = _selectedBalance;
        ApplySelection(_balanceVisuals, _selectedBalance);
    }

    private void OnDurationSelected(int index)
    {
        _selectedDuration = RankingSelectViewSupport.NormalizeSelection(
            index,
            DurationsList.Count);
        _preferences.DurationSelection = (SongDurationFilter)_selectedDuration;
        ApplySelection(_durationVisuals, _selectedDuration);
    }

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
        LayoutMenu();
        SyncSelections();
        ForceMenuLayoutRebuild();
        StartCoroutine(RebuildMenuLayoutNextFrame());
    }

    private void SyncSelections()
    {
        _selectedDifficulty = RankingSelectViewSupport.NormalizeSelection(
            _preferences.DifficultySelection,
            DifficultiesList.Count);
        _selectedBalance = RankingSelectViewSupport.NormalizeSelection(
            _preferences.BalanceSelection,
            BalancesList.Count);
        _selectedDuration = RankingSelectViewSupport.NormalizeSelection(
            (int)_preferences.DurationSelection,
            DurationsList.Count);

        ApplySelection(_difficultyVisuals, _selectedDifficulty);
        ApplySelection(_balanceVisuals, _selectedBalance);
        ApplySelection(_durationVisuals, _selectedDuration);
    }

    internal void SetSearchInProgress(bool searchInProgress)
    {
        // The staged concept intentionally has no primary action yet. Keep the
        // flow hook so post-level navigation can continue using this controller.
    }

    private static void InitializeRowBackground(
        Image backgroundImage,
        Sprite backgroundSprite)
    {
        backgroundImage.sprite = backgroundSprite;
        backgroundImage.raycastTarget = false;
        var backgroundRect = backgroundImage.rectTransform;
        backgroundRect.SetAsFirstSibling();
        backgroundRect.anchorMin = Vector2.zero;
        backgroundRect.anchorMax = Vector2.one;
        backgroundRect.pivot = new Vector2(0.5f, 0.5f);
        backgroundRect.anchoredPosition = Vector2.zero;
        backgroundRect.offsetMin = Vector2.zero;
        backgroundRect.offsetMax = Vector2.zero;
        backgroundRect.localScale = Vector3.one;

        var renderer = backgroundImage.gameObject
            .AddComponent<NineSlicePanelRenderer>();
        renderer.Initialize(
            backgroundSprite,
            backgroundImage,
            Color.white,
            removeExistingChildren: true,
            sliceBordersUv: new Vector4(
                22f / 974f,
                20f / 110f,
                22f / 974f,
                20f / 110f),
            cornerUiSize: new Vector2(2.1f, 2f));
        backgroundImage.enabled = false;
    }

    private static SelectionOptionButtonVisual[] InitializeOptionGroup(
        RectTransform group,
        int selectedIndex,
        Action<int> selectionChanged,
        Sprite unselectedSprite,
        Sprite selectedSprite)
    {
        var buttons = group.GetComponentsInChildren<Button>(true)
            .OrderBy(button => button.transform.GetSiblingIndex())
            .ToArray();
        var visuals = new SelectionOptionButtonVisual[buttons.Length];

        for (var index = 0; index < buttons.Length; index++)
        {
            var button = buttons[index];
            var label = button.GetComponentsInChildren<TMP_Text>(true)
                .First(text => !string.IsNullOrWhiteSpace(text.text));
            var visual = button.gameObject
                .AddComponent<SelectionOptionButtonVisual>();
            visual.Initialize(
                button,
                label,
                unselectedSprite,
                selectedSprite,
                index == selectedIndex);

            var buttonIndex = index;
            button.onClick.AddListener(
                () => selectionChanged(buttonIndex));
            visuals[index] = visual;
        }

        return visuals;
    }

    private static void ApplySelection(
        SelectionOptionButtonVisual[] visuals,
        int selectedIndex)
    {
        for (var index = 0; index < visuals.Length; index++)
        {
            visuals[index].SetSelected(index == selectedIndex);
        }
    }

    private IEnumerator RebuildMenuLayoutNextFrame()
    {
        yield return null;
        LayoutMenu();
        ForceMenuLayoutRebuild();
        LogLayoutGeometry();
    }

    private void LayoutMenu()
    {
        DisableLayoutComponents(_menuRoot);
        DisableLayoutComponents(_headerRow);
        _menuRoot.anchorMin = new Vector2(0.5f, 0.5f);
        _menuRoot.anchorMax = new Vector2(0.5f, 0.5f);
        _menuRoot.pivot = new Vector2(0.5f, 0.5f);
        _menuRoot.anchoredPosition = new Vector2(0f, 6.5f);
        _menuRoot.sizeDelta = new Vector2(112f, 54f);
        _menuRoot.localScale = Vector3.one;

        ConfigureFixedRect(
            _headerRow,
            new Vector2(112f, 10f),
            new Vector2(0f, 22.25f));
        ConfigureFixedRect(
            _difficultyRow,
            new Vector2(112f, 12.65f),
            new Vector2(0f, 7.9f));
        ConfigureFixedRect(
            _balanceRow,
            new Vector2(112f, 12.65f),
            new Vector2(0f, -6.25f));
        ConfigureFixedRect(
            _durationRow,
            new Vector2(112f, 12.65f),
            new Vector2(0f, -20.4f));
        ConfigureFixedRect(
            (RectTransform)_exitButton.transform,
            new Vector2(6.7f, 6.7f),
            new Vector2(49f, 0f));

        ConfigureRowContent(
            _difficultyRow,
            _difficultyIcon,
            _difficultyLabel,
            _difficultyOptions);
        ConfigureRowContent(
            _balanceRow,
            _balanceIcon,
            _balanceLabel,
            _balanceOptions);
        ConfigureRowContent(
            _durationRow,
            _durationIcon,
            _durationLabel,
            _durationOptions);
    }

    private static void ConfigureRowContent(
        RectTransform row,
        Image icon,
        TMP_Text label,
        RectTransform options)
    {
        DisableLayoutComponents(row);
        DisableLayoutComponents(options);
        ConfigureFixedRect(
            icon.rectTransform,
            new Vector2(6f, 6f),
            new Vector2(-49.7f, 0f));
        ConfigureFixedRect(
            label.rectTransform,
            new Vector2(23f, 8f),
            new Vector2(-33f, 0f));
        ConfigureFixedRect(
            options,
            new Vector2(73.4f, 6.9f),
            new Vector2(16.5f, 0f));

        var buttons = options.GetComponentsInChildren<Button>(true)
            .OrderBy(button => button.transform.GetSiblingIndex())
            .ToArray();
        for (var index = 0; index < buttons.Length; index++)
        {
            ConfigureFixedRect(
                (RectTransform)buttons[index].transform,
                new Vector2(14.2f, 6.9f),
                new Vector2(-29.6f + 14.8f * index, 0f));
        }
    }

    private static void DisableLayoutComponents(RectTransform root)
    {
        foreach (var layoutGroup in root.GetComponents<LayoutGroup>())
        {
            layoutGroup.enabled = false;
        }

        foreach (var sizeFitter in root.GetComponents<ContentSizeFitter>())
        {
            sizeFitter.enabled = false;
        }
    }

    private static void ConfigureFixedRect(
        RectTransform rectTransform,
        Vector2 size,
        Vector2 position)
    {
        var layoutElement = rectTransform.GetComponent<LayoutElement>() ??
                            rectTransform.gameObject
                                .AddComponent<LayoutElement>();
        layoutElement.ignoreLayout = true;
        rectTransform.anchorMin = new Vector2(0.5f, 0.5f);
        rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
        rectTransform.pivot = new Vector2(0.5f, 0.5f);
        rectTransform.sizeDelta = size;
        rectTransform.anchoredPosition = position;
        rectTransform.localScale = Vector3.one;
    }

    private void LogLayoutGeometry()
    {
        Plugin.Log.Info(
            "[BeatLeader UI] " +
            DescribeRect("root", _menuRoot) + "; " +
            DescribeRect("header", _headerRow) + "; " +
            DescribeRect("difficulty", _difficultyRow) + "; " +
            DescribeRect("balance", _balanceRow) + "; " +
            DescribeRect("duration", _durationRow) + "; " +
            DescribeRect("exit", (RectTransform)_exitButton.transform) + "; " +
            DescribeRect("difficultyLabel", _difficultyLabel.rectTransform) +
            "; " +
            DescribeRect("difficultyOptions", _difficultyOptions));
    }

    private static string DescribeRect(
        string name,
        RectTransform rectTransform)
    {
        return $"{name}[anchor={rectTransform.anchoredPosition}, " +
               $"size={rectTransform.rect.size}, " +
               $"pivot={rectTransform.pivot}, " +
               $"world={rectTransform.position}]";
    }

    private void ForceMenuLayoutRebuild()
    {
        if (transform is RectTransform viewRoot)
        {
            RankingSelectViewSupport.ForceLayout(viewRoot);
        }
    }
}
