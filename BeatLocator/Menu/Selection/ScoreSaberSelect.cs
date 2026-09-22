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

public sealed class ScoreSaberSelect : BSMLAutomaticViewController
{
    private static TMP_FontAsset? _referenceFont;
    private static bool _referenceFontAttempted;
    private const float BotSpeechLargeFontSize = 3.2f;
    private const float BotSpeechTwoLineFontSize = 2.7f;
    private const float BotSpeechMinimumFontSize = 2.2f;
    private const float BotSpeechFontStep = 0.1f;
    private const float BotSpeechWidthSafetyFactor = 0.78f;
    private static readonly Color ScoreSaberAccent =
        new Color32(255, 211, 24, 255);

    [UIComponent("scoresaber-menu-root")]
    private readonly RectTransform _menuRoot = null!;
    [UIComponent("header-row")]
    private readonly RectTransform _headerRow = null!;
    [UIComponent("scoresaber-provider-logo")]
    private readonly Image _scoreSaberProviderLogo = null!;
    [UIComponent("beatlocator-logo")]
    private readonly Image _beatLocatorLogo = null!;
    [UIComponent("exit-button")]
    private readonly Button _exitButton = null!;
    [UIComponent("exit-artwork")]
    private readonly Image _exitArtwork = null!;

    [UIComponent("difficulty-row")]
    private readonly RectTransform _difficultyRow = null!;
    [UIComponent("duration-row")]
    private readonly RectTransform _durationRow = null!;
    [UIComponent("played-row")]
    private readonly RectTransform _playedRow = null!;
    [UIComponent("difficulty-row-background")]
    private readonly Image _difficultyRowBackground = null!;
    [UIComponent("duration-row-background")]
    private readonly Image _durationRowBackground = null!;
    [UIComponent("played-row-background")]
    private readonly Image _playedRowBackground = null!;
    [UIComponent("difficulty-icon")]
    private readonly Image _difficultyIcon = null!;
    [UIComponent("duration-icon")]
    private readonly Image _durationIcon = null!;
    [UIComponent("played-icon")]
    private readonly Image _playedIcon = null!;
    [UIComponent("difficulty-label")]
    private readonly TMP_Text _difficultyLabel = null!;
    [UIComponent("duration-label")]
    private readonly TMP_Text _durationLabel = null!;
    [UIComponent("played-label")]
    private readonly TMP_Text _playedLabel = null!;
    [UIComponent("difficulty-options")]
    private readonly RectTransform _difficultyOptions = null!;
    [UIComponent("duration-options")]
    private readonly RectTransform _durationOptions = null!;
    [UIComponent("played-options")]
    private readonly RectTransform _playedOptions = null!;

    [UIComponent("modifier-options")]
    private readonly RectTransform _modifierOptions = null!;
    [UIComponent("two-saber-modifier-button")]
    private readonly Button _twoSaberModifierButton = null!;
    [UIComponent("secret-modifier-button")]
    private readonly Button _secretModifierButton = null!;
    [UIComponent("two-saber-modifier-icon")]
    private readonly Image _twoSaberModifierIcon = null!;
    [UIComponent("secret-modifier-icon")]
    private readonly Image _secretModifierIcon = null!;
    [UIComponent("two-saber-modifier-title")]
    private readonly TMP_Text _twoSaberModifierTitle = null!;
    [UIComponent("secret-modifier-title")]
    private readonly TMP_Text _secretModifierTitle = null!;
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
    private int _selectedPlayedFilter;
    private int _selectedDuration;
    private bool _twoSaberEnabled;
    private bool _secretEnabled;
    private bool _botSpeechEnabled;
    private bool _searchInProgress;
    private bool _visualsInitialized;
    private bool _hasBotDialogueForCurrentVisit;
    private string _botDialogueVisit = "returning";
    private MotionScope? _motion;
    private SelectionOptionButtonVisual[] _difficultyVisuals =
        Array.Empty<SelectionOptionButtonVisual>();
    private SelectionOptionButtonVisual[] _playedVisuals =
        Array.Empty<SelectionOptionButtonVisual>();
    private SelectionOptionButtonVisual[] _durationVisuals =
        Array.Empty<SelectionOptionButtonVisual>();
    private AdaptiveModifierButtonLayout? _modifierLayout;
    private ButtonHoverFadeGroup? _hoverFadeGroup;
    private ButtonHoverFadeVisual? _findHoverVisual;
    private ModifierButtonVisual? _twoSaberModifierVisual;
    private ModifierButtonVisual? _secretModifierVisual;

    [UIValue("difficulties")]
    public List<string> DifficultiesList { get; set; } =
        RankingSelectViewSupport.CreateDifficulties();

    [UIValue("playedFilters")]
    public List<string> PlayedFiltersList { get; set; } = new List<string>
    {
        "Any",
        "Played",
        "New"
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
        _selectedPlayedFilter = RankingSelectViewSupport.NormalizeSelection(
            (int)preferences.ScoreSaberPlayedSelection,
            PlayedFiltersList.Count);
        _selectedDuration = RankingSelectViewSupport.NormalizeSelection(
            (int)preferences.DurationSelection,
            DurationsList.Count);
        _twoSaberEnabled = preferences.TwoSaberEnabled;
        _secretEnabled = preferences.SecretDifficultyEnabled;
        _botSpeechEnabled = preferences.BotSpeechEnabled;
        PersistSelections();
    }

    [UIAction("#post-parse")]
    private void PostParse()
    {
        if (_visualsInitialized)
        {
            return;
        }

        var selectedButtonSprite = RankingSelectViewSupport.LoadSprite(
            RankingSelectViewSupport.ScoreSaberSelectedButtonResource);
        var unselectedButtonSprite = RankingSelectViewSupport.LoadSprite(
            RankingSelectViewSupport.ScoreSaberButtonResource);
        var hoverButtonSprite = RankingSelectViewSupport.LoadSprite(
            RankingSelectViewSupport.ScoreSaberHoverButtonResource);
        var longSelectedButtonSprite = RankingSelectViewSupport.LoadSprite(
            RankingSelectViewSupport.ScoreSaberLongSelectedButtonResource);
        var longUnselectedButtonSprite = RankingSelectViewSupport.LoadSprite(
            RankingSelectViewSupport.ScoreSaberLongButtonResource);
        var longHoverButtonSprite = RankingSelectViewSupport.LoadSprite(
            RankingSelectViewSupport.ScoreSaberLongHoverButtonResource);
        var rowBackgroundSprite = RankingSelectViewSupport.LoadSlicedSprite(
            RankingSelectViewSupport.ScoreSaberRowBackgroundResource,
            new Vector4(22f, 20f, 22f, 20f),
            10f,
            1.8f);
        var selectedModifierSprite = RankingSelectViewSupport.LoadSlicedSprite(
            RankingSelectViewSupport.ScoreSaberSelectedModifierResource,
            new Vector4(18f, 18f, 18f, 18f),
            10f,
            1.8f);
        var unselectedModifierSprite = RankingSelectViewSupport.LoadSlicedSprite(
            RankingSelectViewSupport.ScoreSaberUnselectedModifierResource,
            new Vector4(18f, 18f, 18f, 18f),
            10f,
            1.8f);
        var hoverModifierSprite = RankingSelectViewSupport.LoadSlicedSprite(
            RankingSelectViewSupport.ScoreSaberHoverModifierResource,
            new Vector4(18f, 18f, 18f, 18f),
            10f);

        LayoutMenu();
        ApplyReferenceTypography();
        TintProviderArtwork();
        _hoverFadeGroup = _menuRoot.gameObject
            .AddComponent<ButtonHoverFadeGroup>();

        _exitArtwork.sprite = RankingSelectViewSupport.LoadSprite(
            RankingSelectViewSupport.CrossButtonResource);
        _exitArtwork.preserveAspect = true;
        StaticSpriteButtonVisual.Initialize(
            _exitButton,
            _exitArtwork,
            new Vector2(1.2f, 1.2f));
        var exitHoverVisual = _exitButton.gameObject
            .AddComponent<ButtonHoverFadeVisual>();
        exitHoverVisual.Initialize(
            _exitButton,
            RankingSelectViewSupport.LoadSprite(
                RankingSelectViewSupport.CrossHoverResource),
            _exitArtwork,
            _hoverFadeGroup,
            matchArtworkRect: true,
            crossFadeArtwork: true);

        _botArtwork.sprite = RankingSelectViewSupport.LoadSprite(
            RankingSelectViewSupport.ScoreSaberBotResource);
        _botArtwork.preserveAspect = true;
        StaticSpriteButtonVisual.Initialize(
            _botButton,
            _botArtwork,
            new Vector2(0.6f, 0.6f));
        _botSpeechBackground.sprite = RankingSelectViewSupport.LoadDuotoneSprite(
            RankingSelectViewSupport.BotSpeechBubbleResource,
            new Color32(13, 18, 33, 255),
            ScoreSaberAccent);
        _botSpeechBackground.raycastTarget = false;

        _findArtwork.sprite = RankingSelectViewSupport.LoadSprite(
            RankingSelectViewSupport.ScoreSaberFindMapsButtonResource);
        _findArtwork.raycastTarget = false;
        StaticSpriteButtonVisual.Initialize(
            _findButton,
            _findArtwork,
            new Vector2(0.6f, 0.8f));
        _findHoverVisual = _findButton.gameObject
            .AddComponent<ButtonHoverFadeVisual>();
        _findHoverVisual.Initialize(
            _findButton,
            RankingSelectViewSupport.LoadSprite(
                RankingSelectViewSupport.ScoreSaberFindMapsHoverResource),
            _findArtwork,
            _hoverFadeGroup);
        _findButton.interactable = !_searchInProgress;
        _findHoverVisual.SetHoverEnabled(!_searchInProgress);

        InitializeRowBackground(_difficultyRowBackground, rowBackgroundSprite);
        InitializeRowBackground(_durationRowBackground, rowBackgroundSprite);
        InitializeRowBackground(_playedRowBackground, rowBackgroundSprite);

        _difficultyVisuals = InitializeOptionGroup(
            _difficultyOptions,
            _selectedDifficulty,
            OnDifficultySelected,
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
        _playedVisuals = InitializeOptionGroup(
            _playedOptions,
            _selectedPlayedFilter,
            OnPlayedFilterSelected,
            longUnselectedButtonSprite,
            longSelectedButtonSprite,
            longHoverButtonSprite,
            _hoverFadeGroup,
            2.2f);

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

    private void OnPlayedFilterSelected(int index)
    {
        _selectedPlayedFilter = RankingSelectViewSupport.NormalizeSelection(
            index,
            PlayedFiltersList.Count);
        _preferences.ScoreSaberPlayedSelection =
            (ScoreSaberPlayedFilter)_selectedPlayedFilter;
        ApplySelection(_playedVisuals, _selectedPlayedFilter);
    }

    private void OnDurationSelected(int index)
    {
        _selectedDuration = RankingSelectViewSupport.NormalizeSelection(
            index,
            DurationsList.Count);
        _preferences.DurationSelection = (SongDurationFilter)_selectedDuration;
        ApplySelection(_durationVisuals, _selectedDuration);
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
        _flowCoordinator.FindScoreSaberMapAsync(
            (ScoreSaberPlayedFilter)_selectedPlayedFilter,
            RankingSelectViewSupport.StarBuffer,
            _twoSaberEnabled,
            _secretEnabled,
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

    internal bool PrepareForFirstPresentation()
    {
        if (_visualsInitialized) return false;

        ParseWithFallback();
        return _visualsInitialized;
    }

    protected override void DidActivate(
        bool firstActivation,
        bool addedToHierarchy,
        bool screenSystemEnabling)
    {
        base.DidActivate(
            firstActivation && !_visualsInitialized,
            addedToHierarchy,
            screenSystemEnabling);
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
        _selectedPlayedFilter = RankingSelectViewSupport.NormalizeSelection(
            (int)_preferences.ScoreSaberPlayedSelection,
            PlayedFiltersList.Count);
        _selectedDuration = RankingSelectViewSupport.NormalizeSelection(
            (int)_preferences.DurationSelection,
            DurationsList.Count);
        _twoSaberEnabled = _preferences.TwoSaberEnabled;
        _secretEnabled = _preferences.SecretDifficultyEnabled;
        _botSpeechEnabled = _preferences.BotSpeechEnabled;

        ApplySelection(_difficultyVisuals, _selectedDifficulty);
        ApplySelection(_playedVisuals, _selectedPlayedFilter);
        ApplySelection(_durationVisuals, _selectedDuration);
        _twoSaberModifierVisual?.SetSelected(_twoSaberEnabled);
        _secretModifierVisual?.SetSelected(_secretEnabled);
    }

    private void PersistSelections()
    {
        _preferences.DifficultySelection = _selectedDifficulty;
        _preferences.ScoreSaberPlayedSelection =
            (ScoreSaberPlayedFilter)_selectedPlayedFilter;
        _preferences.DurationSelection = (SongDurationFilter)_selectedDuration;
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

    private void SetBotSpeech(string speech, bool animate = true)
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
            $"[ScoreSaber Bot Speech] chars={normalizedSpeech.Length}, " +
            $"lines={(preparedSpeech.Contains("\n") ? 2 : 1)}, " +
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
                var firstLine = string.Join(" ", words, 0, breakIndex);
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

    private static string JoinBotSpeechLines(string[] words, int breakIndex)
    {
        return string.Join(" ", words, 0, breakIndex) + "\n" +
               string.Join(
                   " ",
                   words,
                   breakIndex,
                   words.Length - breakIndex);
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
            Provider = "scoresaber",
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

        _hasBotDialogueForCurrentVisit = true;
        ApplyBotSpeechVisibility(true);
        SetBotSpeech(presentation.Text);
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

    private void TintProviderArtwork()
    {
        TintImage(_difficultyIcon, ScoreSaberAccent);
        TintImage(_durationIcon, ScoreSaberAccent);
        TintImage(_playedIcon, ScoreSaberAccent);
        TintImage(_twoSaberModifierIcon, ScoreSaberAccent);
        TintImage(_secretModifierIcon, ScoreSaberAccent);
        TintImage(_scoreSaberProviderLogo, Color.white);
        TintImage(_beatLocatorLogo, Color.white);
    }

    private void ApplyReferenceTypography()
    {
        var referenceFont = GetReferenceFont();
        if (referenceFont == null)
        {
            return;
        }

        foreach (var label in new[]
                 {
                     _difficultyLabel,
                     _durationLabel,
                     _playedLabel,
                     _twoSaberModifierTitle,
                     _secretModifierTitle,
                     _twoSaberModifierDescription,
                     _secretModifierDescription,
                     _botSpeechText
                 })
        {
            label.font = referenceFont;
        }

        _twoSaberModifierTitle.fontStyle = FontStyles.Bold;
        _secretModifierTitle.fontStyle = FontStyles.Bold;
        _twoSaberModifierDescription.fontStyle = FontStyles.Normal;
        _secretModifierDescription.fontStyle = FontStyles.Normal;
        _botSpeechText.fontStyle = FontStyles.Normal;
    }

    private static TMP_FontAsset? GetReferenceFont()
    {
        if (_referenceFontAttempted)
        {
            return _referenceFont;
        }

        _referenceFontAttempted = true;
        try
        {
            var systemFont = Font.CreateDynamicFontFromOSFont("Arial", 48);
            if (!systemFont)
            {
                return null;
            }

            systemFont.hideFlags = HideFlags.DontUnloadUnusedAsset;
            _referenceFont = TMP_FontAsset.CreateFontAsset(systemFont);
            _referenceFont.atlasPopulationMode = AtlasPopulationMode.Dynamic;
            _referenceFont.hideFlags = HideFlags.DontUnloadUnusedAsset;
            Plugin.Log.Info("[ScoreSaber UI] Using Arial for reference typography.");
            return _referenceFont;
        }
        catch (Exception exception)
        {
            Plugin.Log.Warn($"[ScoreSaber UI] Sans font unavailable: {exception.Message}");
            return null;
        }
    }

    private static void TintImage(Image image, Color color)
    {
        image.color = Color.white;
        image.canvasRenderer.SetColor(color);
    }

    private static void InitializeRowBackground(
        Image backgroundImage,
        Sprite backgroundSprite)
    {
        backgroundImage.raycastTarget = false;
        var backgroundRect = backgroundImage.rectTransform;
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
        ButtonHoverFadeGroup hoverGroup,
        float horizontalVisualBleed = 0f)
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
            var referenceFont = GetReferenceFont();
            if (referenceFont != null)
            {
                label.font = referenceFont;
            }
            var visual = button.gameObject
                .AddComponent<SelectionOptionButtonVisual>();
            visual.Initialize(
                button,
                label,
                unselectedSprite,
            selectedSprite,
            hoverSprite,
            hoverGroup,
                index == selectedIndex,
                horizontalVisualBleed);
            if (referenceFont != null)
            {
                label.fontStyle = FontStyles.Italic;
                label.fontSize = buttons.Length == 3 ? 3f : 2.8f;
                label.enableAutoSizing = true;
                label.fontSizeMin = 2.35f;
                label.fontSizeMax = label.fontSize;
            }

            var buttonIndex = index;
            button.onClick.AddListener(() => selectionChanged(buttonIndex));
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
        ConfigureFixedRect(_menuRoot, new Vector2(112f, 136f),
            new Vector2(0f, 25.8f));
        ConfigureFixedRect(_headerRow, new Vector2(112f, 10f),
            new Vector2(0f, 22.25f));
        ConfigureFixedRect(_scoreSaberProviderLogo.rectTransform,
            new Vector2(6.7f, 6.7f), new Vector2(-38.5f, 0f));
        ConfigureFixedRect(_beatLocatorLogo.rectTransform,
            new Vector2(60f, 7f), Vector2.zero);
        ConfigureFixedRect(_difficultyRow, new Vector2(112f, 12.65f),
            new Vector2(0f, 7.4f));
        ConfigureFixedRect(_durationRow, new Vector2(112f, 12.65f),
            new Vector2(0f, -6.15f));
        ConfigureFixedRect(_playedRow, new Vector2(112f, 12.65f),
            new Vector2(0f, -19.7f));
        ConfigureFixedRect(_modifierOptions, new Vector2(112f, 12.65f),
            new Vector2(0f, -33.25f));
        ConfigureFixedRect(_botSpeechRow, new Vector2(112f, 12f),
            new Vector2(0f, -46.475f));
        ConfigureFixedRect((RectTransform)_findButton.transform,
            new Vector2(112f, 11.5f), new Vector2(0f, -59.125f));
        ConfigureFixedRect((RectTransform)_exitButton.transform,
            new Vector2(6.7f, 6.7f), new Vector2(63.5f, -14.35f));
        ConfigureFixedRect(_exitArtwork.rectTransform,
            new Vector2(5.1f, 5.1f), Vector2.zero);

        ConfigureRowContent(
            _difficultyRow,
            _difficultyIcon,
            _difficultyLabel,
            _difficultyOptions);
        ConfigureRowContent(
            _durationRow,
            _durationIcon,
            _durationLabel,
            _durationOptions);
        ConfigureRowContent(
            _playedRow,
            _playedIcon,
            _playedLabel,
            _playedOptions);
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
        ConfigureFixedRect((RectTransform)_botButton.transform,
            new Vector2(11f, 11f), new Vector2(-46.7f, 0f));
        ConfigureFixedRect(_botArtwork.rectTransform,
            new Vector2(10.7f, 10f), Vector2.zero);
        ConfigureFixedRect(_botSpeechBackground.rectTransform,
            new Vector2(93.3f, 11.45f), new Vector2(9.35f, 0f));
        ConfigureFixedRect(_botSpeechText.rectTransform,
            new Vector2(88f, 8.4f), new Vector2(10.5f, 0.1f));
        _botSpeechText.enableAutoSizing = false;
        _botSpeechText.fontSize = BotSpeechLargeFontSize;
        _botSpeechText.enableWordWrapping = false;
        _botSpeechText.overflowMode = TextOverflowModes.Overflow;
        _botSpeechText.alignment = TextAlignmentOptions.MidlineLeft;
        _botSpeechText.margin = Vector4.zero;
        ConfigureFixedRect(_findArtwork.rectTransform,
            new Vector2(112f, 11.5f), Vector2.zero);
    }

    private static void ConfigureRowContent(
        RectTransform row,
        Image icon,
        TMP_Text label,
        RectTransform options)
    {
        DisableLayoutComponents(row);
        DisableLayoutComponents(options);
        ConfigureFixedRect(icon.rectTransform,
            new Vector2(6f, 6f), new Vector2(-50.2f, 0f));
        ConfigureFixedRect(label.rectTransform,
            new Vector2(23f, 8f), new Vector2(-33.6f, -0.65f));
        label.fontWeight = FontWeight.SemiBold;
        label.alignment = TextAlignmentOptions.MidlineLeft;
        label.lineSpacing = -8f;
        ConfigureFixedRect(options,
            new Vector2(73.4f, 6.9f), new Vector2(15.7f, 0f));

        var buttons = options.GetComponentsInChildren<Button>(true)
            .OrderBy(button => button.transform.GetSiblingIndex())
            .ToArray();
        var spacing = buttons.Length == 3 ? 0f : 0.6f;
        var buttonWidth =
            (73.4f - spacing * (buttons.Length - 1)) / buttons.Length;
        var firstCenter = -73.4f * 0.5f + buttonWidth * 0.5f;
        for (var index = 0; index < buttons.Length; index++)
        {
            ConfigureFixedRect(
                (RectTransform)buttons[index].transform,
                new Vector2(buttonWidth, 6.9f),
                new Vector2(
                    firstCenter + index * (buttonWidth + spacing),
                    0f));
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
        ConfigureLeftAnchoredRect(icon.rectTransform, iconSize,
            new Vector2(8.4f, 0f));
        ConfigureStretchedTextRect(title.rectTransform,
            15f, 1f, 1.1f, 3.1f);
        ConfigureStretchedTextRect(description.rectTransform,
            15f, 1f, -3f, 4.5f);
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

    private void ForceMenuLayoutRebuild()
    {
        RankingSelectViewSupport.ForceLayout(_menuRoot);
        _modifierLayout?.Refresh();
    }

    private void LogLayoutGeometry()
    {
        Plugin.Log.Info(
            "[ScoreSaber UI] " +
            DescribeRect("root", _menuRoot) + "; " +
            DescribeRect("difficulty", _difficultyRow) + "; " +
            DescribeRect("duration", _durationRow) + "; " +
            DescribeRect("played", _playedRow) + "; " +
            DescribeRect("modifiers", _modifierOptions) + "; " +
            DescribeRect("botSpeech", _botSpeechRow) + "; " +
            DescribeRect("find", (RectTransform)_findButton.transform) + "; " +
            DescribeRect("exit", (RectTransform)_exitButton.transform));
    }

    private static string DescribeRect(string name, RectTransform rectTransform)
    {
        return $"{name}[anchor={rectTransform.anchoredPosition}, " +
               $"size={rectTransform.rect.size}, " +
               $"world={rectTransform.position}]";
    }
}
