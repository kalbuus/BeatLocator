using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using BeatSaberMarkupLanguage.Attributes;
using BeatSaberMarkupLanguage.ViewControllers;
using BeatLocator.Dialogue;
using BeatLocator.Settings;
using MotionUtils;
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
    private const float BotSpeechLargeFontSize = 3.2f;
    private const float BotSpeechTwoLineFontSize = 2.7f;
    private const float BotSpeechMinimumFontSize = 2.2f;
    private const float BotSpeechFontStep = 0.1f;
    private const float BotSpeechWidthSafetyFactor = 0.78f;

    [UIComponent("beatleader-menu-root")]
    private readonly RectTransform _menuRoot = null!;
    [UIComponent("header-row")]
    private readonly RectTransform _headerRow = null!;
    [UIComponent("beatleader-provider-logo")]
    private readonly Image _beatLeaderProviderLogo = null!;
    [UIComponent("beatlocator-logo")]
    private readonly Image _beatLocatorLogo = null!;
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
    [UIComponent("modifier-options")]
    private readonly RectTransform _modifierOptions = null!;
    [UIComponent("played-modifier-button")]
    private readonly Button _playedModifierButton = null!;
    [UIComponent("two-saber-modifier-button")]
    private readonly Button _twoSaberModifierButton = null!;
    [UIComponent("secret-modifier-button")]
    private readonly Button _secretModifierButton = null!;
    [UIComponent("played-modifier-icon")]
    private readonly Image _playedModifierIcon = null!;
    [UIComponent("two-saber-modifier-icon")]
    private readonly Image _twoSaberModifierIcon = null!;
    [UIComponent("secret-modifier-icon")]
    private readonly Image _secretModifierIcon = null!;
    [UIComponent("played-modifier-title")]
    private readonly TMP_Text _playedModifierTitle = null!;
    [UIComponent("two-saber-modifier-title")]
    private readonly TMP_Text _twoSaberModifierTitle = null!;
    [UIComponent("secret-modifier-title")]
    private readonly TMP_Text _secretModifierTitle = null!;
    [UIComponent("played-modifier-description")]
    private readonly TMP_Text _playedModifierDescription = null!;
    [UIComponent("two-saber-modifier-description")]
    private readonly TMP_Text _twoSaberModifierDescription = null!;
    [UIComponent("secret-modifier-description")]
    private readonly TMP_Text _secretModifierDescription = null!;
    [UIComponent("bot-speech-row")]
    private readonly RectTransform _botSpeechRow = null!;
    [UIComponent("bot-button")]
    private readonly Button _botButton = null!;
    [UIComponent("bot-artwork")]
    private readonly Image _botArtwork = null!;
    [UIComponent("bot-speech-background")]
    private readonly Image _botSpeechBackground = null!;
    [UIComponent("bot-speech-text")]
    private readonly TMP_Text _botSpeechText = null!;
    [UIComponent("find-button")]
    private readonly Button _findButton = null!;
    [UIComponent("find-artwork")]
    private readonly Image _findArtwork = null!;

    private BeatLocatorFlowCoordinator _flowCoordinator = null!;
    private RankingSearchPreferences _preferences = null!;
    private BotDialogueService _botDialogueService = null!;
    private BotDialogueSessionState _botDialogueSessionState = null!;
    private int _selectedDifficulty;
    private int _selectedBalance;
    private int _selectedDuration;
    private bool _playedEnabled;
    private bool _twoSaberEnabled;
    private bool _secretEnabled;
    private bool _botSpeechEnabled;
    private bool _searchInProgress;
    private bool _visualsInitialized;
    private bool _hasBotDialogueForCurrentVisit;
    private string _botDialogueVisit = "returning";
    private Sprite? _botSprite;
    private MotionScope? _motion;
    private SelectionOptionButtonVisual[] _difficultyVisuals =
        Array.Empty<SelectionOptionButtonVisual>();
    private SelectionOptionButtonVisual[] _balanceVisuals =
        Array.Empty<SelectionOptionButtonVisual>();
    private SelectionOptionButtonVisual[] _durationVisuals =
        Array.Empty<SelectionOptionButtonVisual>();
    private AdaptiveModifierButtonLayout? _modifierLayout;
    private ButtonHoverFadeGroup? _hoverFadeGroup;
    private ButtonHoverFadeVisual? _findHoverVisual;
    private ModifierButtonVisual? _playedModifierVisual;
    private ModifierButtonVisual? _twoSaberModifierVisual;
    private ModifierButtonVisual? _secretModifierVisual;

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
        RankingSearchPreferences preferences,
        BotDialogueService botDialogueService,
        BotDialogueSessionState botDialogueSessionState)
    {
        _flowCoordinator = flowCoordinator;
        _preferences = preferences;
        _botDialogueService = botDialogueService;
        _botDialogueSessionState = botDialogueSessionState;
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
        _botSpeechEnabled = preferences.BotSpeechEnabled;

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
        var hoverButtonSprite = RankingSelectViewSupport.LoadSprite(
            RankingSelectViewSupport.HoverButtonResource);
        var rowBackgroundSprite = RankingSelectViewSupport.LoadSlicedSprite(
            RankingSelectViewSupport.SelectionRowBackgroundResource,
            new Vector4(22f, 20f, 22f, 20f),
            10f,
            1.8f);
        var selectedModifierSprite =
            RankingSelectViewSupport.LoadSlicedSprite(
                RankingSelectViewSupport.SelectedModifierResource,
                new Vector4(18f, 18f, 18f, 18f),
                10f,
                1.8f);
        var unselectedModifierSprite =
            RankingSelectViewSupport.LoadSlicedSprite(
                RankingSelectViewSupport.UnselectedModifierResource,
                new Vector4(18f, 18f, 18f, 18f),
                10f,
                1.8f);
        var hoverModifierSprite =
            RankingSelectViewSupport.LoadSlicedSprite(
                RankingSelectViewSupport.HoverModifierResource,
                new Vector4(18f, 18f, 18f, 18f),
                10f);
        var findHoverSprite = RankingSelectViewSupport.LoadSprite(
            RankingSelectViewSupport.FindMapsHoverButtonResource);

        LayoutMenu();

        _hoverFadeGroup = _menuRoot.gameObject
            .AddComponent<ButtonHoverFadeGroup>();

        _exitArtwork.sprite = RankingSelectViewSupport.LoadSprite(
            RankingSelectViewSupport.CrossButtonResource);
        _exitArtwork.preserveAspect = true;
        StaticSpriteButtonVisual.Initialize(_exitButton, _exitArtwork);
        var exitHoverVisual = _exitButton.gameObject
            .AddComponent<ButtonHoverFadeVisual>();
        exitHoverVisual.Initialize(
            _exitButton,
            RankingSelectViewSupport.LoadSprite(
                RankingSelectViewSupport.CrossHoverResource),
            _exitArtwork,
            _hoverFadeGroup);

        _botSprite ??= RankingSelectViewSupport.LoadSprite(
            _botDialogueService.DefaultSpriteResource);
        _botArtwork.sprite = _botSprite;
        _botArtwork.preserveAspect = true;
        StaticSpriteButtonVisual.Initialize(_botButton, _botArtwork);

        _botSpeechBackground.sprite = RankingSelectViewSupport.LoadSprite(
            RankingSelectViewSupport.BotSpeechBubbleResource);
        _botSpeechBackground.raycastTarget = false;

        _findArtwork.sprite = RankingSelectViewSupport.LoadSprite(
            RankingSelectViewSupport.FindMapsButtonResource);
        _findArtwork.raycastTarget = false;
        StaticSpriteButtonVisual.Initialize(_findButton, _findArtwork);
        _findHoverVisual = _findButton.gameObject
            .AddComponent<ButtonHoverFadeVisual>();
        _findHoverVisual.Initialize(
            _findButton,
            findHoverSprite,
            _findArtwork,
            _hoverFadeGroup);
        _findButton.interactable = !_searchInProgress;
        _findHoverVisual.SetHoverEnabled(!_searchInProgress);

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
            selectedButtonSprite,
            hoverButtonSprite,
            _hoverFadeGroup);
        _balanceVisuals = InitializeOptionGroup(
            _balanceOptions,
            _selectedBalance,
            OnBalanceSelected,
            unselectedButtonSprite,
            selectedButtonSprite,
            hoverButtonSprite,
            _hoverFadeGroup);
        _durationVisuals = InitializeOptionGroup(
            _durationOptions,
            _selectedDuration,
            OnDurationSelected,
            unselectedButtonSprite,
            selectedButtonSprite,
            hoverButtonSprite,
            _hoverFadeGroup);

        _playedModifierVisual = InitializeModifier(
            _playedModifierButton,
            _playedModifierIcon,
            _playedModifierTitle,
            _playedModifierDescription,
            unselectedModifierSprite,
            selectedModifierSprite,
            hoverModifierSprite,
            _hoverFadeGroup,
            _playedEnabled);
        _twoSaberModifierVisual = InitializeModifier(
            _twoSaberModifierButton,
            _twoSaberModifierIcon,
            _twoSaberModifierTitle,
            _twoSaberModifierDescription,
            unselectedModifierSprite,
            selectedModifierSprite,
            hoverModifierSprite,
            _hoverFadeGroup,
            _twoSaberEnabled);
        _secretModifierVisual = InitializeModifier(
            _secretModifierButton,
            _secretModifierIcon,
            _secretModifierTitle,
            _secretModifierDescription,
            unselectedModifierSprite,
            selectedModifierSprite,
            hoverModifierSprite,
            _hoverFadeGroup,
            _secretEnabled);

        _modifierLayout = _modifierOptions.gameObject
            .AddComponent<AdaptiveModifierButtonLayout>();
        _modifierLayout.Initialize(_modifierOptions, 0.65f, 12.65f);

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

    [UIAction("togglePlayed")]
    private void OnTogglePlayed()
    {
        _playedEnabled = !_playedEnabled;
        _preferences.PlayedEnabled = _playedEnabled;
        _playedModifierVisual?.SetSelected(_playedEnabled);
    }

    [UIAction("toggleTwoSaber")]
    private void OnToggleTwoSaber()
    {
        _twoSaberEnabled = !_twoSaberEnabled;
        _preferences.TwoSaberEnabled = _twoSaberEnabled;
        _twoSaberModifierVisual?.SetSelected(_twoSaberEnabled);
    }

    [UIAction("toggleSecretDifficulty")]
    private void OnToggleSecretDifficulty()
    {
        _secretEnabled = !_secretEnabled;
        _preferences.SecretDifficultyEnabled = _secretEnabled;
        _secretModifierVisual?.SetSelected(_secretEnabled);
    }

    [UIAction("toggleBotSpeech")]
    private void OnToggleBotSpeech()
    {
        _botSpeechEnabled = !_botSpeechEnabled;
        _preferences.BotSpeechEnabled = _botSpeechEnabled;
        if (_botSpeechEnabled)
        {
            if (_hasBotDialogueForCurrentVisit)
            {
                ApplyBotSpeechVisibility(true);
            }
            else
            {
                ShowBotDialogue(BotDialogueEvents.SettingsOpened);
            }
        }
        else
        {
            _motion?.Kill("bot-speech-typing");
            ApplyBotSpeechVisibility(false);
        }
    }

    [UIAction("findPressed")]
    private void OnFindPressed()
    {
        if (_searchInProgress)
        {
            return;
        }

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
        _botDialogueVisit = _botDialogueSessionState.RegisterSettingsVisit();
        _hasBotDialogueForCurrentVisit = false;
        if (_botSpeechEnabled)
        {
            ShowBotDialogue(BotDialogueEvents.SettingsOpened);
        }
        else
        {
            ApplyBotSpeechVisibility(false);
        }
        ForceMenuLayoutRebuild();
        StartCoroutine(RebuildMenuLayoutNextFrame());
    }

    protected override void DidDeactivate(
        bool removedFromHierarchy,
        bool screenSystemDisabling)
    {
        _motion?.Kill("bot-speech-typing");
        base.DidDeactivate(removedFromHierarchy, screenSystemDisabling);
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
        _playedEnabled = _preferences.PlayedEnabled;
        _twoSaberEnabled = _preferences.TwoSaberEnabled;
        _secretEnabled = _preferences.SecretDifficultyEnabled;
        _botSpeechEnabled = _preferences.BotSpeechEnabled;

        ApplySelection(_difficultyVisuals, _selectedDifficulty);
        ApplySelection(_balanceVisuals, _selectedBalance);
        ApplySelection(_durationVisuals, _selectedDuration);
        _playedModifierVisual?.SetSelected(_playedEnabled);
        _twoSaberModifierVisual?.SetSelected(_twoSaberEnabled);
        _secretModifierVisual?.SetSelected(_secretEnabled);
    }

    internal void SetSearchInProgress(bool searchInProgress)
    {
        _searchInProgress = searchInProgress;
        if (_findButton)
        {
            _findButton.interactable = !searchInProgress;
            _findHoverVisual?.SetHoverEnabled(!searchInProgress);
        }
    }

    internal void ShowSearchFailureDialogue(string eventId)
    {
        ShowBotDialogue(eventId);
    }

    internal void SetBotImage(Sprite botSprite)
    {
        if (!botSprite)
        {
            throw new ArgumentNullException(nameof(botSprite));
        }

        _botSprite = botSprite;
        if (_botArtwork)
        {
            _botArtwork.sprite = botSprite;
            _botArtwork.preserveAspect = true;
        }
    }

    internal void SetBotImage(string embeddedResourceName)
    {
        SetBotImage(RankingSelectViewSupport.LoadSprite(
            embeddedResourceName));
    }

    internal void ResetBotImage()
    {
        SetBotImage(RankingSelectViewSupport.LoadSprite(
            RankingSelectViewSupport.DefaultBotResource));
    }

    internal void SetBotSpeech(string speech, bool animate = true)
    {
        if (!_visualsInitialized || !_botSpeechText)
        {
            return;
        }

        _motion ??= MotionUtils.Motion.For(this);
        _motion.Kill("bot-speech-typing");
        var normalizedSpeech = speech ?? string.Empty;
        _botSpeechText.maxVisibleCharacters = int.MaxValue;
        var preparedSpeech = PrepareBotSpeechLayout(normalizedSpeech);
        _botSpeechText.text = preparedSpeech;
        _botSpeechText.ForceMeshUpdate(true, true);
        Plugin.Log.Debug(
            $"[Bot Speech] chars={normalizedSpeech.Length}, " +
            $"lines={(preparedSpeech.Contains("\n") ? 2 : 1)}, " +
            $"firstLineChars={GetFirstLineLength(preparedSpeech)}, " +
            $"font={_botSpeechText.fontSize:0.00}.");
        if (!animate || normalizedSpeech.Length == 0)
        {
            return;
        }

        var duration = Mathf.Clamp(
            normalizedSpeech.Length * 0.025f,
            1.1f,
            3.2f);
        _motion.RevealText(
            "bot-speech-typing",
            _botSpeechText,
            TextRevealSpec.Typewriter(duration));
    }

    private string PrepareBotSpeechLayout(string speech)
    {
        _botSpeechText.enableAutoSizing = false;
        _botSpeechText.enableWordWrapping = false;
        _botSpeechText.overflowMode = TextOverflowModes.Overflow;
        _botSpeechText.alignment = TextAlignmentOptions.MidlineLeft;

        var availableWidth = Mathf.Max(
            1f,
            _botSpeechText.rectTransform.rect.width *
            BotSpeechWidthSafetyFactor);
        _botSpeechText.fontSize = BotSpeechLargeFontSize;
        if (MeasureBotSpeechLine(speech) <= availableWidth)
        {
            return speech;
        }

        var words = speech.Split(
            new[] { ' ' },
            StringSplitOptions.RemoveEmptyEntries);
        if (words.Length < 2)
        {
            _botSpeechText.fontSize = BotSpeechMinimumFontSize;
            return speech;
        }

        var fallbackBreak = 1;
        var fallbackScore = float.MaxValue;
        for (var fontStep = 0;
             BotSpeechTwoLineFontSize - fontStep * BotSpeechFontStep >=
             BotSpeechMinimumFontSize - 0.001f;
             fontStep++)
        {
            var fontSize = BotSpeechTwoLineFontSize -
                           fontStep * BotSpeechFontStep;
            _botSpeechText.fontSize = fontSize;
            var bestBreak = -1;
            var bestFirstLineWidth = -1f;

            for (var breakIndex = 1;
                 breakIndex < words.Length;
                 breakIndex++)
            {
                var firstLine = string.Join(
                    " ",
                    words,
                    0,
                    breakIndex);
                var secondLine = string.Join(
                    " ",
                    words,
                    breakIndex,
                    words.Length - breakIndex);
                var firstWidth = MeasureBotSpeechLine(firstLine);
                var secondWidth = MeasureBotSpeechLine(secondLine);
                var widestLine = Mathf.Max(firstWidth, secondWidth);

                if (fontSize <= BotSpeechMinimumFontSize + 0.001f)
                {
                    var overflow =
                        Mathf.Max(0f, firstWidth - availableWidth) +
                        Mathf.Max(0f, secondWidth - availableWidth);
                    var fallbackCandidateScore = overflow * 1000f +
                                                 Mathf.Abs(
                                                     availableWidth -
                                                     firstWidth);
                    if (fallbackCandidateScore < fallbackScore)
                    {
                        fallbackScore = fallbackCandidateScore;
                        fallbackBreak = breakIndex;
                    }
                }

                if (widestLine <= availableWidth &&
                    firstWidth > bestFirstLineWidth)
                {
                    bestFirstLineWidth = firstWidth;
                    bestBreak = breakIndex;
                }
            }

            if (bestBreak >= 0)
            {
                return JoinBotSpeechLines(words, bestBreak);
            }
        }

        _botSpeechText.fontSize = BotSpeechMinimumFontSize;
        return JoinBotSpeechLines(words, fallbackBreak);
    }

    private float MeasureBotSpeechLine(string line)
    {
        return _botSpeechText.GetPreferredValues(line).x;
    }

    private static string JoinBotSpeechLines(
        string[] words,
        int breakIndex)
    {
        return string.Join(" ", words, 0, breakIndex) + "\n" +
               string.Join(
                   " ",
                   words,
                   breakIndex,
                   words.Length - breakIndex);
    }

    private static int GetFirstLineLength(string speech)
    {
        var lineBreak = speech.IndexOf('\n');
        return lineBreak >= 0 ? lineBreak : speech.Length;
    }

    private void ShowBotDialogue(string eventId)
    {
        if (!_visualsInitialized || !_botSpeechEnabled)
        {
            return;
        }

        var context = new BotDialogueContext
        {
            Event = eventId,
            Provider = "beatleader",
            Visit = _botDialogueVisit,
            PreviousOutcome = _botDialogueSessionState.PreviousOutcome,
        };
        if (!_botDialogueService.TrySelect(context, out var presentation) ||
            presentation == null)
        {
            _motion?.Kill("bot-speech-typing");
            SetBotSpeech(string.Empty, false);
            _hasBotDialogueForCurrentVisit = false;
            ApplyBotSpeechVisibility(false);
            return;
        }

        TrySetBotImage(presentation.SpriteResource);
        _hasBotDialogueForCurrentVisit = true;
        ApplyBotSpeechVisibility(true);
        SetBotSpeech(presentation.Text);
    }

    private void TrySetBotImage(string spriteResource)
    {
        try
        {
            SetBotImage(spriteResource);
        }
        catch (Exception exception)
        {
            Plugin.Log.Error(
                $"[Bot Dialogue] Could not apply sprite '{spriteResource}': " +
                exception);
            ResetBotImage();
        }
    }

    private void ApplyBotSpeechVisibility(bool visible)
    {
        if (!_visualsInitialized)
        {
            return;
        }

        _botSpeechBackground.gameObject.SetActive(visible);
        _botSpeechText.gameObject.SetActive(visible);
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
        Sprite selectedSprite,
        Sprite hoverSprite,
        ButtonHoverFadeGroup hoverGroup)
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
                hoverSprite,
                hoverGroup,
                index == selectedIndex);

            var buttonIndex = index;
            button.onClick.AddListener(
                () => selectionChanged(buttonIndex));
            visuals[index] = visual;
        }

        return visuals;
    }

    private static ModifierButtonVisual InitializeModifier(
        Button button,
        Image icon,
        TMP_Text title,
        TMP_Text description,
        Sprite unselectedSprite,
        Sprite selectedSprite,
        Sprite hoverSprite,
        ButtonHoverFadeGroup hoverGroup,
        bool selected)
    {
        var visual = button.gameObject.AddComponent<ModifierButtonVisual>();
        visual.Initialize(
            button,
            icon,
            title,
            description,
            unselectedSprite,
            selectedSprite,
            hoverSprite,
            hoverGroup,
            selected);
        return visual;
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
        _menuRoot.anchoredPosition = new Vector2(0f, 28.5f);
        _menuRoot.sizeDelta = new Vector2(112f, 136f);
        _menuRoot.localScale = Vector3.one;

        ConfigureFixedRect(
            _headerRow,
            new Vector2(112f, 10f),
            new Vector2(0f, 22.25f));
        ConfigureFixedRect(
            _beatLeaderProviderLogo.rectTransform,
            new Vector2(6.7f, 6.7f),
            new Vector2(-33.5f, 0f));
        ConfigureFixedRect(
            _beatLocatorLogo.rectTransform,
            new Vector2(63.35f, 7f),
            new Vector2(4.5f, 0f));
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
            _modifierOptions,
            new Vector2(112f, 12.65f),
            new Vector2(0f, -34.55f));
        ConfigureFixedRect(
            _botSpeechRow,
            new Vector2(112f, 12f),
            new Vector2(0f, -48.55f));
        ConfigureFixedRect(
            (RectTransform)_findButton.transform,
            new Vector2(112f, 11.5f),
            new Vector2(0f, -62.55f));
        ConfigureFixedRect(
            (RectTransform)_exitButton.transform,
            new Vector2(6.7f, 6.7f),
            new Vector2(63.5f, -14.35f));

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
        ConfigureModifierContent(
            _playedModifierButton,
            _playedModifierIcon,
            _playedModifierTitle,
            _playedModifierDescription,
            new Vector2(7.3f, 8f));
        ConfigureModifierContent(
            _twoSaberModifierButton,
            _twoSaberModifierIcon,
            _twoSaberModifierTitle,
            _twoSaberModifierDescription,
            new Vector2(8f, 8f));
        ConfigureModifierContent(
            _secretModifierButton,
            _secretModifierIcon,
            _secretModifierTitle,
            _secretModifierDescription,
            new Vector2(6.4f, 8f));
        ConfigureBotContent();
        _modifierLayout?.Refresh();
    }

    private void ConfigureBotContent()
    {
        DisableLayoutComponents(_botSpeechRow);
        ConfigureFixedRect(
            (RectTransform)_botButton.transform,
            new Vector2(11f, 11f),
            new Vector2(-49.5f, 0f));
        ConfigureFixedRect(
            _botArtwork.rectTransform,
            new Vector2(10.7f, 10f),
            Vector2.zero);
        ConfigureFixedRect(
            _botSpeechBackground.rectTransform,
            new Vector2(99f, 11.45f),
            new Vector2(6.5f, 0f));
        ConfigureFixedRect(
            _botSpeechText.rectTransform,
            new Vector2(91f, 8.4f),
            new Vector2(9f, 0.1f));
        _botSpeechText.enableAutoSizing = false;
        _botSpeechText.fontSize = BotSpeechLargeFontSize;
        _botSpeechText.enableWordWrapping = false;
        _botSpeechText.overflowMode = TextOverflowModes.Overflow;
        _botSpeechText.alignment = TextAlignmentOptions.MidlineLeft;
        _botSpeechText.margin = Vector4.zero;
        ConfigureFixedRect(
            _findArtwork.rectTransform,
            new Vector2(112f, 11.5f),
            Vector2.zero);
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

    private static void ConfigureModifierContent(
        Button button,
        Image icon,
        TMP_Text title,
        TMP_Text description,
        Vector2 iconSize)
    {
        var buttonRect = (RectTransform)button.transform;
        DisableLayoutComponents(buttonRect);
        ConfigureLeftAnchoredRect(
            icon.rectTransform,
            iconSize,
            new Vector2(6.2f, 0f));
        ConfigureStretchedTextRect(
            title.rectTransform,
            11.5f,
            1f,
            1.1f,
            3.1f);
        ConfigureStretchedTextRect(
            description.rectTransform,
            11.5f,
            1f,
            -3f,
            4.5f);
    }

    private static void ConfigureLeftAnchoredRect(
        RectTransform rectTransform,
        Vector2 size,
        Vector2 position)
    {
        IgnoreLayout(rectTransform);
        rectTransform.anchorMin = new Vector2(0f, 0.5f);
        rectTransform.anchorMax = new Vector2(0f, 0.5f);
        rectTransform.pivot = new Vector2(0.5f, 0.5f);
        rectTransform.sizeDelta = size;
        rectTransform.anchoredPosition = position;
        rectTransform.localScale = Vector3.one;
    }

    private static void ConfigureStretchedTextRect(
        RectTransform rectTransform,
        float left,
        float right,
        float centerY,
        float height)
    {
        IgnoreLayout(rectTransform);
        rectTransform.anchorMin = new Vector2(0f, 0.5f);
        rectTransform.anchorMax = new Vector2(1f, 0.5f);
        rectTransform.pivot = new Vector2(0f, 0.5f);
        rectTransform.offsetMin = new Vector2(left, centerY - height * 0.5f);
        rectTransform.offsetMax = new Vector2(-right, centerY + height * 0.5f);
        rectTransform.localScale = Vector3.one;
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
        IgnoreLayout(rectTransform);
        rectTransform.anchorMin = new Vector2(0.5f, 0.5f);
        rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
        rectTransform.pivot = new Vector2(0.5f, 0.5f);
        rectTransform.sizeDelta = size;
        rectTransform.anchoredPosition = position;
        rectTransform.localScale = Vector3.one;
    }

    private static void IgnoreLayout(RectTransform rectTransform)
    {
        var layoutElement = rectTransform.GetComponent<LayoutElement>() ??
                            rectTransform.gameObject
                                .AddComponent<LayoutElement>();
        layoutElement.ignoreLayout = true;
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
            DescribeRect("modifiers", _modifierOptions) + "; " +
            DescribeRect("botSpeech", _botSpeechRow) + "; " +
            DescribeRect("find", (RectTransform)_findButton.transform) + "; " +
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
