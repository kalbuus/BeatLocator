using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BeatLocator.EvaluationManagers;
using BeatSaberMarkupLanguage;
using BeatSaberMarkupLanguage.Attributes;
using BeatSaberMarkupLanguage.ViewControllers;
using HMUI;
using TMPro;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;
using Zenject;

namespace BeatLocator.Menu;

public sealed class RouletteAnimationViewController : BSMLAutomaticViewController
{
    private const string PreviewCdnBaseUrl = "https://cfcdn.beatsaver.com/";
    private const int CardCount = 16;
    private const int WinnerIndex = 13;
    private const float CardSize = 20f;
    private const float CardSpacing = 6f;
    private const float SpinDuration = 4.5f;
    private const float ViewportWidth = 120f;
    private const float ViewportHeight = 42f;
    private const float MaximumCenterScaleBonus = 0.25f;
    private const float DummyCoverShade = 0.15f;
    private const float InterfaceScale = 1.5f;
    private const float InterfaceVerticalOffset = -8f;
    private const float SongDetailsWidth = 62f;
    private const float SongDetailsHeight = 36f;
    private const float SubNameVerticalOffset = -0.35f;

    [UIComponent("roulette-root")]
    private RectTransform _rouletteRoot = null!;
    [UIComponent("roulette-viewport")]
    private RectTransform _viewport = null!;
    [UIComponent("song-details")]
    private RectTransform _songDetails = null!;
    [UIComponent("song-title-row")]
    private RectTransform _songTitleRow = null!;
    [UIComponent("song-title")]
    private RectTransform _songTitle = null!;
    [UIComponent("song-sub-name")]
    private RectTransform _songSubName = null!;
    [UIComponent("song-author")]
    private RectTransform _songAuthor = null!;
    [UIComponent("song-mapper")]
    private RectTransform _songMapper = null!;
    [UIComponent("song-difficulty")]
    private RectTransform _songDifficulty = null!;
    [UIComponent("song-actions")]
    private RectTransform _songActions = null!;
    [UIComponent("skip-button")]
    private Button _skipButton = null!;
    [UIComponent("primary-button")]
    private Button _primaryButton = null!;

    [UIValue("statusText")]
    public string StatusText { get; private set; } = "Preparing roulette...";
    [UIValue("songTitleText")]
    public string SongTitleText { get; private set; } = "Unknown song";
    [UIValue("songSubNameText")]
    public string SongSubNameText { get; private set; } = string.Empty;
    [UIValue("songAuthorText")]
    public string SongAuthorText { get; private set; } = "Song by unknown artist";
    [UIValue("songMapperText")]
    public string SongMapperText { get; private set; } = "Mapped by unknown mapper";
    [UIValue("songDifficultyText")]
    public string SongDifficultyText { get; private set; } = "UNKNOWN  •  0.00 ★";
    [UIValue("primaryButtonText")]
    public string PrimaryButtonText { get; private set; } = "DOWNLOAD";

    private readonly List<ImageView> _dummyCovers = new List<ImageView>();
    private BeatLocatorFlowCoordinator _flowCoordinator = null!;
    private SongPreviewPlayer _songPreviewPlayer = null!;
    private Image _viewportBackground = null!;
    private Color _viewportBackgroundColor;
    private RectTransform _strip = null!;
    private CanvasGroup _stripCanvasGroup = null!;
    private ImageView _winnerCover = null!;
    private CanvasGroup _winnerCanvasGroup = null!;
    private RectTransform _centerMarker = null!;
    private CanvasGroup _centerMarkerCanvasGroup = null!;
    private RectTransform _heroCoverTransform = null!;
    private ImageView _heroCover = null!;
    private CanvasGroup _heroCoverCanvasGroup = null!;
    private Button _heroCoverButton = null!;
    private PreviewHoverHandler _previewHoverHandler = null!;
    private CanvasGroup _previewOverlayCanvasGroup = null!;
    private TMP_Text _previewIcon = null!;
    private AudioSource _previewAudioSource = null!;
    private AudioClip? _previewAudioClip;
    private Task<AudioClip>? _previewLoadTask;
    private bool _previewPaused;
    private bool _menuMusicPausedByPreview;
    private Coroutine? _previewMonitorCoroutine;
    private readonly List<CanvasGroup> _songDetailCanvasGroups = new List<CanvasGroup>();
    private Sprite? _loadedCoverSprite;
    private Texture2D? _loadedCoverTexture;
    private Sprite? _roundedCardSprite;
    private Texture2D? _roundedCardTexture;
    private Material? _roundEdgeMaterial;
    private Task<Sprite>? _coverLoadTask;
    private Coroutine? _spinCoroutine;
    private Coroutine? _revealCoroutine;
    private bool _coverLoaded;
    private bool _spinFinished;
    private bool _secretDifficulty;
    private int _runId;
    private EvaluatedDifficulty? _selectedDifficulty;
    private string? _coverUrl;
    private string? _fallbackCoverUrl;
    private string? _previewUrl;
    private PrimaryButtonState _primaryButtonState;

    private enum PrimaryButtonState
    {
        Download,
        Downloading,
        Play,
        Starting
    }
    
    private PluginConfig _config = null!;

    [Inject]
    private void Construct(
        BeatLocatorFlowCoordinator flowCoordinator,
        SongPreviewPlayer songPreviewPlayer,
        PluginConfig config)
    {
        _flowCoordinator = flowCoordinator;
        _songPreviewPlayer = songPreviewPlayer;
        _config = config;
    }

    internal void SetResult(
        EvaluatedDifficulty selectedDifficulty,
        bool secretDifficulty)
    {
        _runId++;
        ReleaseSelectedMedia();

        _selectedDifficulty = selectedDifficulty;
        _secretDifficulty = secretDifficulty;
        SelectCoverUrls(
            selectedDifficulty.Map.FullCoverImage,
            selectedDifficulty.Map.CoverImage);
        _previewUrl = BuildPreviewUrl(selectedDifficulty.Map.Hash);

        SongTitleText = ValueOrFallback(selectedDifficulty.Map.Name, "Unknown song");
        SongSubNameText = selectedDifficulty.Map.SubName ?? string.Empty;
        SongAuthorText =
            $"Song by {ValueOrFallback(selectedDifficulty.Map.Author, "unknown artist")}";
        SongMapperText =
            $"Mapped by {ValueOrFallback(selectedDifficulty.Map.Mapper, "unknown mapper")}";
        SongDifficultyText = FormatDifficulty(selectedDifficulty);
        PrimaryButtonText = "DOWNLOAD";
        _primaryButtonState = PrimaryButtonState.Download;

        NotifyPropertyChanged(nameof(SongTitleText));
        NotifyPropertyChanged(nameof(SongSubNameText));
        NotifyPropertyChanged(nameof(SongAuthorText));
        NotifyPropertyChanged(nameof(SongMapperText));
        NotifyPropertyChanged(nameof(SongDifficultyText));
        NotifyPropertyChanged(nameof(PrimaryButtonText));
        UpdateDifficultyVisibility();
    }

    [UIAction("#post-parse")]
    private void PostParse()
    {
        _rouletteRoot.localScale = Vector3.one * InterfaceScale;
        _rouletteRoot.anchoredPosition += new Vector2(0f, InterfaceVerticalOffset);

        _roundEdgeMaterial = Resources.FindObjectsOfTypeAll<Material>()
            .FirstOrDefault(material => material.name == "UINoGlowRoundEdge");

        if (_roundEdgeMaterial == null)
        {
            Plugin.Log.Warn("Could not find the UINoGlowRoundEdge material used by BetterSongSearch.");
        }

        PrepareViewport();
        CreateStrip();
        CreateDummyCovers();
        CreateWinnerCover();
        CreateCenterMarker();
        CreateHeroCover();
        PrepareSongDetails();
        PreparePreviewAudio();
    }

    protected override void DidActivate(
        bool firstActivation,
        bool addedToHierarchy,
        bool screenSystemEnabling)
    {
        base.DidActivate(firstActivation, addedToHierarchy, screenSystemEnabling);

        if (addedToHierarchy && _selectedDifficulty != null)
        {
            StartDemo();
        }
    }

    protected override void DidDeactivate(
        bool removedFromHierarchy,
        bool screenSystemDisabling)
    {
        base.DidDeactivate(removedFromHierarchy, screenSystemDisabling);

        if (!removedFromHierarchy) return;

        _runId++;
        StopPreviewPlayback();
        StopAnimations();
    }

    [UIAction("skipPressed")]
    private void OnSkipPressed()
    {
        if (_primaryButtonState == PrimaryButtonState.Downloading ||
            _primaryButtonState == PrimaryButtonState.Starting)
        {
            return;
        }

        _flowCoordinator.ShowBeatLeaderSelect();
    }

    [UIAction("primaryPressed")]
    private async void OnPrimaryPressed()
    {
        if (_primaryButtonState == PrimaryButtonState.Play &&
            _selectedDifficulty != null)
        {
            var playRunId = _runId;
            _primaryButtonState = PrimaryButtonState.Starting;
            SetPrimaryButtonText("STARTING");
            _primaryButton.interactable = false;
            _skipButton.interactable = false;
            StopPreviewPlayback();

            var started = await _flowCoordinator.PlayMapAsync(_selectedDifficulty);
            if (!started && playRunId == _runId)
            {
                _primaryButtonState = PrimaryButtonState.Play;
                SetPrimaryButtonText("PLAY");
                _primaryButton.interactable = true;
                _skipButton.interactable = true;
            }
            return;
        }

        if (_primaryButtonState == PrimaryButtonState.Downloading ||
            _primaryButtonState == PrimaryButtonState.Starting ||
            _selectedDifficulty == null)
        {
            return;
        }

        var runId = _runId;
        _primaryButtonState = PrimaryButtonState.Downloading;
        SetPrimaryButtonText("INSTALLING");
        _primaryButton.interactable = false;
        _skipButton.interactable = false;
        StopPreviewPlayback();

        var outcome = await _flowCoordinator.DownloadMapAsync(_selectedDifficulty);
        if (runId != _runId) return;

        if (outcome == MapDownloadOutcome.Installed ||
            outcome == MapDownloadOutcome.AlreadyInstalled)
        {
            _primaryButtonState = PrimaryButtonState.Play;
            SetPrimaryButtonText("PLAY");
            _primaryButton.interactable = true;
            _skipButton.interactable = true;
            return;
        }

        _primaryButtonState = PrimaryButtonState.Download;
        SetPrimaryButtonText("RETRY");
        _primaryButton.interactable = true;
        _skipButton.interactable = true;
    }

    private void PrepareViewport()
    {
        _viewportBackground = _viewport.GetComponent<Image>();
        _viewportBackgroundColor = _viewportBackground.color;

        var layoutElement = _viewport.GetComponent<LayoutElement>();
        if (layoutElement == null)
        {
            layoutElement = _viewport.gameObject.AddComponent<LayoutElement>();
        }

        layoutElement.minWidth = ViewportWidth;
        layoutElement.preferredWidth = ViewportWidth;
        layoutElement.minHeight = ViewportHeight;
        layoutElement.preferredHeight = ViewportHeight;

        var layoutGroup = _viewport.GetComponent<HorizontalLayoutGroup>();
        if (layoutGroup != null)
        {
            layoutGroup.enabled = false;
        }

        if (_viewport.GetComponent<RectMask2D>() == null)
        {
            _viewport.gameObject.AddComponent<RectMask2D>();
        }
    }

    private void CreateStrip()
    {
        var stripObject = new GameObject(
            "RouletteStrip",
            typeof(RectTransform),
            typeof(CanvasGroup));

        _strip = stripObject.GetComponent<RectTransform>();
        _strip.SetParent(_viewport, false);
        _strip.anchorMin = new Vector2(0f, 0.5f);
        _strip.anchorMax = new Vector2(0f, 0.5f);
        _strip.pivot = new Vector2(0f, 0.5f);
        _strip.sizeDelta = new Vector2(
            CardCount * CardSize + (CardCount - 1) * CardSpacing,
            CardSize);
        _stripCanvasGroup = stripObject.GetComponent<CanvasGroup>();
    }

    private void CreateDummyCovers()
    {
        var step = CardSize + CardSpacing;
        _roundedCardSprite = CreateDummyCoverSprite();

        for (var index = 0; index < CardCount; index++)
        {
            var coverObject = new GameObject(
                $"DummyCover-{index}",
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(ImageView));

            var coverTransform = coverObject.GetComponent<RectTransform>();
            coverTransform.SetParent(_strip, false);
            coverTransform.anchorMin = new Vector2(0f, 0.5f);
            coverTransform.anchorMax = new Vector2(0f, 0.5f);
            coverTransform.pivot = new Vector2(0.5f, 0.5f);
            coverTransform.sizeDelta = new Vector2(CardSize, CardSize);
            coverTransform.anchoredPosition = new Vector2(
                CardSize * 0.5f + index * step,
                0f);

            var image = coverObject.GetComponent<ImageView>();
            image.sprite = _roundedCardSprite;
            image.type = Image.Type.Simple;
            image.material = _roundEdgeMaterial;
            image.color = new Color(DummyCoverShade, DummyCoverShade, DummyCoverShade, 1f);
            image.raycastTarget = false;
            image.maskable = true;

            _dummyCovers.Add(image);
        }
    }

    private void CreateWinnerCover()
    {
        var coverObject = new GameObject(
            "WinnerCover",
            typeof(RectTransform),
            typeof(CanvasRenderer),
            typeof(ImageView),
            typeof(CanvasGroup));

        var coverTransform = coverObject.GetComponent<RectTransform>();
        coverTransform.SetParent(_dummyCovers[WinnerIndex].rectTransform, false);
        coverTransform.anchorMin = Vector2.zero;
        coverTransform.anchorMax = Vector2.one;
        coverTransform.offsetMin = Vector2.zero;
        coverTransform.offsetMax = Vector2.zero;

        _winnerCover = coverObject.GetComponent<ImageView>();
        _winnerCover.color = new Color(1f, 1f, 1f, 0f);
        _winnerCover.preserveAspect = true;
        _winnerCover.raycastTarget = false;
        _winnerCover.maskable = true;
        _winnerCover.material = _roundEdgeMaterial;

        _winnerCanvasGroup = coverObject.GetComponent<CanvasGroup>();
        _winnerCanvasGroup.alpha = 0f;
        _winnerCanvasGroup.interactable = false;
        _winnerCanvasGroup.blocksRaycasts = false;
    }

    private void CreateCenterMarker()
    {
        var markerObject = new GameObject(
            "CenterMarker",
            typeof(RectTransform),
            typeof(CanvasRenderer),
            typeof(ImageView),
            typeof(CanvasGroup));

        _centerMarker = markerObject.GetComponent<RectTransform>();
        _centerMarker.SetParent(_viewport, false);
        _centerMarker.anchorMin = new Vector2(0.5f, 0f);
        _centerMarker.anchorMax = new Vector2(0.5f, 1f);
        _centerMarker.pivot = new Vector2(0.5f, 0.5f);
        _centerMarker.sizeDelta = new Vector2(0.35f, 0f);
        _centerMarker.anchoredPosition = Vector2.zero;

        var markerImage = markerObject.GetComponent<ImageView>();
        markerImage.color = new Color(0.2f, 0.8f, 1f, 0.8f);
        markerImage.raycastTarget = false;
        markerImage.maskable = true;
        _centerMarkerCanvasGroup = markerObject.GetComponent<CanvasGroup>();
    }

    private void CreateHeroCover()
    {
        var heroObject = new GameObject(
            "SongInfoHeroCover",
            typeof(RectTransform),
            typeof(CanvasRenderer),
            typeof(ImageView),
            typeof(CanvasGroup),
            typeof(Button),
            typeof(PreviewHoverHandler));

        _heroCoverTransform = heroObject.GetComponent<RectTransform>();
        _heroCoverTransform.SetParent(_viewport, false);
        _heroCoverTransform.anchorMin = new Vector2(0.5f, 0.5f);
        _heroCoverTransform.anchorMax = new Vector2(0.5f, 0.5f);
        _heroCoverTransform.pivot = new Vector2(0.5f, 0.5f);

        _heroCover = heroObject.GetComponent<ImageView>();
        _heroCover.material = _roundEdgeMaterial;
        _heroCover.preserveAspect = true;
        _heroCover.raycastTarget = true;

        _heroCoverButton = heroObject.GetComponent<Button>();
        _heroCoverButton.transition = Selectable.Transition.None;
        _heroCoverButton.targetGraphic = _heroCover;
        _heroCoverButton.interactable = false;
        _heroCoverButton.onClick.AddListener(TogglePreviewPlayback);

        _previewHoverHandler = heroObject.GetComponent<PreviewHoverHandler>();
        _previewHoverHandler.enabled = false;

        _heroCoverCanvasGroup = heroObject.GetComponent<CanvasGroup>();
        _heroCoverCanvasGroup.alpha = 0f;
        _heroCoverCanvasGroup.interactable = true;
        _heroCoverCanvasGroup.blocksRaycasts = true;
        CreatePreviewOverlay();
        _previewHoverHandler.HoverChanged = OnPreviewHoverChanged;
        heroObject.SetActive(false);
    }

    private void CreatePreviewOverlay()
    {
        var overlayObject = new GameObject(
            "PreviewOverlay",
            typeof(RectTransform),
            typeof(CanvasGroup));
        var overlayTransform = overlayObject.GetComponent<RectTransform>();
        overlayTransform.SetParent(_heroCoverTransform, false);
        overlayTransform.anchorMin = Vector2.zero;
        overlayTransform.anchorMax = Vector2.one;
        overlayTransform.offsetMin = Vector2.zero;
        overlayTransform.offsetMax = Vector2.zero;

        _previewOverlayCanvasGroup = overlayObject.GetComponent<CanvasGroup>();
        _previewOverlayCanvasGroup.alpha = 0f;
        _previewOverlayCanvasGroup.interactable = false;
        _previewOverlayCanvasGroup.blocksRaycasts = false;

        var shadeObject = new GameObject(
            "PreviewShade",
            typeof(RectTransform),
            typeof(CanvasRenderer),
            typeof(ImageView));
        var shadeTransform = shadeObject.GetComponent<RectTransform>();
        shadeTransform.SetParent(overlayTransform, false);
        shadeTransform.anchorMin = Vector2.zero;
        shadeTransform.anchorMax = Vector2.one;
        shadeTransform.offsetMin = Vector2.zero;
        shadeTransform.offsetMax = Vector2.zero;

        var shadeImage = shadeObject.GetComponent<ImageView>();
        shadeImage.sprite = _roundedCardSprite;
        shadeImage.material = _roundEdgeMaterial;
        shadeImage.color = new Color(0f, 0f, 0f, 0.28f);
        shadeImage.raycastTarget = false;

        _previewIcon = BeatSaberUI.CreateText(
            overlayTransform,
            "▶",
            Vector2.zero,
            new Vector2(CardSize, CardSize));
        _previewIcon.alignment = TextAlignmentOptions.Center;
        _previewIcon.fontSize = 8f;
        _previewIcon.color = new Color(1f, 1f, 1f, 0.92f);
        _previewIcon.raycastTarget = false;
        _previewIcon.rectTransform.anchorMin = Vector2.zero;
        _previewIcon.rectTransform.anchorMax = Vector2.one;
        _previewIcon.rectTransform.offsetMin = Vector2.zero;
        _previewIcon.rectTransform.offsetMax = Vector2.zero;
    }

    private void PreparePreviewAudio()
    {
        _previewAudioSource = gameObject.GetComponent<AudioSource>() ??
                              gameObject.AddComponent<AudioSource>();
        _previewAudioSource.playOnAwake = false;
        _previewAudioSource.loop = false;
        _previewAudioSource.volume = 0.7f;
    }

    private void PrepareSongDetails()
    {
        _songDetails.anchorMin = new Vector2(0.5f, 0.5f);
        _songDetails.anchorMax = new Vector2(0.5f, 0.5f);
        _songDetails.pivot = new Vector2(0.5f, 0.5f);
        _songDetails.anchoredPosition = new Vector2(23f, -1f);

        var contentSizeFitter = _songDetails.GetComponent<ContentSizeFitter>();
        if (contentSizeFitter != null)
        {
            contentSizeFitter.enabled = false;
        }

        EnforceSongDetailsBounds();
        ConfigureSingleLineText(_songTitle, 5.5f, 2.6f);
        ConfigureSingleLineText(_songSubName, 2.8f, 1.8f);
        ConfigureSingleLineText(_songAuthor, 3.2f, 2.1f);
        ConfigureSingleLineText(_songMapper, 3.2f, 2.1f);
        ConfigureSingleLineText(_songDifficulty, 3.2f, 2.1f);
        UpdateDifficultyVisibility();

        _songDetailCanvasGroups.Clear();
        AddSongDetailCanvasGroup(_songTitleRow);
        AddSongDetailCanvasGroup(_songAuthor);
        AddSongDetailCanvasGroup(_songMapper);
        AddSongDetailCanvasGroup(_songDifficulty);
        AddSongDetailCanvasGroup(_songActions);

        _songDetails.gameObject.SetActive(false);
    }

    private void UpdateDifficultyVisibility()
    {
        if (_songDifficulty != null)
        {
            _songDifficulty.gameObject.SetActive(!_secretDifficulty);
        }
    }

    private void EnforceSongDetailsBounds()
    {
        _songDetails.sizeDelta = new Vector2(SongDetailsWidth, SongDetailsHeight);
    }

    private static void ConfigureSingleLineText(
        RectTransform textTransform,
        float maximumFontSize,
        float minimumFontSize)
    {
        var text = textTransform.GetComponent<TMP_Text>();
        text.enableAutoSizing = true;
        text.fontSizeMax = maximumFontSize;
        text.fontSizeMin = minimumFontSize;
        text.enableWordWrapping = false;
        text.overflowMode = TextOverflowModes.Ellipsis;
    }

    private void AddSongDetailCanvasGroup(RectTransform element)
    {
        _songDetailCanvasGroups.Add(GetOrAddCanvasGroup(element.gameObject));
    }

    private static CanvasGroup GetOrAddCanvasGroup(GameObject target)
    {
        return target.GetComponent<CanvasGroup>() ?? target.AddComponent<CanvasGroup>();
    }

    private void StartDemo()
    {
        StopAnimations();
        StopPreviewPlayback();

        var runId = ++_runId;
        _coverLoaded = _loadedCoverSprite != null;
        _spinFinished = false;
        _winnerCover.color = Color.white;
        _winnerCover.sprite = _loadedCoverSprite ?? _roundedCardSprite;
        _winnerCanvasGroup.alpha = 0f;
        _strip.anchoredPosition = Vector2.zero;
        ResetSongInfoState();
        ResetWinnerPlaceholder();
        ResetCoverScales();

        SetStatus($"Selecting {SongTitleText}...");
        if (!_coverLoaded && !string.IsNullOrWhiteSpace(_coverUrl))
        {
            _ = LoadCoverAsync(runId);
        }
        else if (!_coverLoaded)
        {
            _coverLoaded = true;
        }
        _spinCoroutine = StartCoroutine(AnimateRoulette(runId));
    }

    private async Task LoadCoverAsync(int runId)
    {
        try
        {
            var coverSprite = _loadedCoverSprite ??
                              await (_coverLoadTask ??= DownloadCoverSpriteAsync());

            if (runId != _runId) return;

            _loadedCoverSprite = coverSprite;
            _coverLoadTask = null;
            _winnerCover.sprite = coverSprite;
            _coverLoaded = true;
            Plugin.Log.Info(
                $"Roulette cover loaded: {coverSprite.texture.width} x " +
                $"{coverSprite.texture.height}.");
            if (_spinFinished)
            {
                RevealWinner();
            }
        }
        catch (Exception exception)
        {
            _coverLoadTask = null;
            if (runId != _runId) return;

            Plugin.Log.Error($"Could not load the roulette cover: {exception}");
            _winnerCover.sprite = _roundedCardSprite;
            _coverLoaded = true;
            if (_spinFinished)
            {
                RevealWinner();
            }
        }
    }

    private async Task<Sprite> DownloadCoverSpriteAsync()
    {
        var coverUrl = _coverUrl
                       ?? throw new InvalidOperationException(
                           "BeatLeader did not provide a cover URL.");
        try
        {
            return await DownloadCoverSpriteFromUrlAsync(coverUrl);
        }
        catch (Exception exception) when (_fallbackCoverUrl != null)
        {
            Plugin.Log.Warn(
                $"Primary roulette cover could not be decoded; trying the API fallback: " +
                exception.Message);
            return await DownloadCoverSpriteFromUrlAsync(_fallbackCoverUrl);
        }
    }

    private async Task<Sprite> DownloadCoverSpriteFromUrlAsync(string coverUrl)
    {
        using var downloadHandler = new DownloadHandlerTexture();
        using var request = UnityWebRequest.Get(coverUrl);
        request.SetRequestHeader("User-Agent", "BeatLocator/0.0.1");
        request.downloadHandler = downloadHandler;
        request.disposeDownloadHandlerOnDispose = false;

        var operation = request.SendWebRequest();
        while (!operation.isDone)
        {
            await Task.Delay(20);
        }

        if (request.result != UnityWebRequest.Result.Success)
        {
            throw new InvalidOperationException(
                $"Cover request failed: {request.responseCode} {request.error}");
        }

        var texture = downloadHandler.texture
                      ?? throw new InvalidOperationException("The cover request returned no texture.");
        texture.name = "BeatLocator Roulette Cover";
        texture.wrapMode = TextureWrapMode.Clamp;
        texture.filterMode = FilterMode.Bilinear;

        var sprite = Sprite.Create(
            texture,
            new Rect(0f, 0f, texture.width, texture.height),
            Vector2.zero,
            100f);
        sprite.name = "BeatLocator Roulette Cover";

        _loadedCoverTexture = texture;
        return sprite;
    }

    private Sprite CreateDummyCoverSprite()
    {
        const int textureSize = 256;
        var pixels = new Color32[textureSize * textureSize];

        for (var index = 0; index < pixels.Length; index++)
        {
            pixels[index] = new Color32(255, 255, 255, 255);
        }

        _roundedCardTexture = new Texture2D(
            textureSize,
            textureSize,
            TextureFormat.RGBA32,
            false)
        {
            name = "BeatLocator Rounded Dummy Cover",
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear
        };
        _roundedCardTexture.SetPixels32(pixels);
        _roundedCardTexture.Apply(false, true);

        var sprite = Sprite.Create(
            _roundedCardTexture,
            new Rect(0f, 0f, textureSize, textureSize),
            new Vector2(0.5f, 0.5f),
            100f);
        sprite.name = "BeatLocator Rounded Dummy Cover";
        return sprite;
    }

    private IEnumerator AnimateRoulette(int runId)
    {
        // Let BSML finish its first layout pass before reading the viewport width.
        yield return null;
        LayoutRebuilder.ForceRebuildLayoutImmediate(_viewport);

        if (_viewport.rect.width < 1f || _viewport.rect.height < 1f)
        {
            Plugin.Log.Warn(
                $"Roulette viewport was collapsed to {_viewport.rect.width:0.##} x " +
                $"{_viewport.rect.height:0.##}; applying a fallback size.");
            _viewport.sizeDelta = new Vector2(ViewportWidth, ViewportHeight);
        }

        Plugin.Log.Info(
            $"Roulette viewport size: {_viewport.rect.width:0.##} x {_viewport.rect.height:0.##}.");

        var startX = 0f;
        var targetX = GetTargetPosition(WinnerIndex);
        var elapsed = 0f;

        while (elapsed < SpinDuration * (1/_config.SpeedAnimationValue) && runId == _runId)
        {
            elapsed += Time.unscaledDeltaTime;
            var progress = Mathf.Clamp01(elapsed / (SpinDuration * (1/_config.SpeedAnimationValue)));
            var easedProgress = 1f - Mathf.Pow(1f - progress, 5f);

            var position = _strip.anchoredPosition;
            position.x = Mathf.LerpUnclamped(startX, targetX, easedProgress);
            _strip.anchoredPosition = position;
            UpdateCoverScales();

            yield return null;
        }

        if (runId != _runId) yield break;

        var finalPosition = _strip.anchoredPosition;
        finalPosition.x = targetX;
        _strip.anchoredPosition = finalPosition;
        UpdateCoverScales();
        _spinCoroutine = null;
        _spinFinished = true;

        if (_coverLoaded)
        {
            RevealWinner();
        }
        else
        {
            SetStatus("Winner selected. Loading cover...");
        }
    }

    private float GetTargetPosition(int selectedIndex)
    {
        var step = CardSize + CardSpacing;
        var selectedCardCenter = CardSize * 0.5f + selectedIndex * step;
        return _viewport.rect.width * 0.5f - selectedCardCenter;
    }

    private void UpdateCoverScales()
    {
        var viewportCenter = _viewport.rect.width * 0.5f;
        var influenceRadius = _viewport.rect.width * 0.25f;

        foreach (var cover in _dummyCovers)
        {
            var coverCenter = _strip.anchoredPosition.x + cover.rectTransform.anchoredPosition.x;
            var normalizedDistance = Mathf.Clamp01(
                Mathf.Abs(coverCenter - viewportCenter) / influenceRadius);
            var proximity = 1f - normalizedDistance;
            var sinusoidalInfluence = Mathf.Sin(proximity * Mathf.PI * 0.5f);
            var scale = 1f + MaximumCenterScaleBonus * sinusoidalInfluence;

            cover.rectTransform.localScale = new Vector3(scale, scale, 1f);
        }
    }

    private void ResetCoverScales()
    {
        foreach (var cover in _dummyCovers)
        {
            cover.rectTransform.localScale = Vector3.one;
        }
    }

    private void ResetWinnerPlaceholder()
    {
        var winnerCard = _dummyCovers[WinnerIndex];
        winnerCard.sprite = _roundedCardSprite;
        winnerCard.color = new Color(DummyCoverShade, DummyCoverShade, DummyCoverShade, 1f);
    }

    private void ResetSongInfoState()
    {
        _strip.gameObject.SetActive(true);
        _stripCanvasGroup.alpha = 1f;
        _centerMarker.gameObject.SetActive(true);
        _centerMarkerCanvasGroup.alpha = 1f;

        _heroCoverTransform.gameObject.SetActive(false);
        _heroCoverCanvasGroup.alpha = 0f;
        _heroCover.color = Color.white;
        _heroCoverButton.interactable = false;
        _previewHoverHandler.enabled = false;
        _previewOverlayCanvasGroup.alpha = 0f;
        SetPreviewIcon(false);
        _viewportBackground.enabled = true;
        _viewportBackground.color = _viewportBackgroundColor;
        _songDetails.gameObject.SetActive(false);
        _skipButton.interactable = false;
        _primaryButton.interactable = false;

        foreach (var detailCanvasGroup in _songDetailCanvasGroups)
        {
            detailCanvasGroup.alpha = 0f;
        }
    }

    private void RevealWinner()
    {
        Plugin.Log.Info(
            $"Revealing roulette cover for {SongTitleText}; " +
            $"sprite assigned: {_winnerCover.sprite != null}.");
        SetStatus(SongTitleText);
        _revealCoroutine = StartCoroutine(FadeInWinner(_runId));
    }

    private IEnumerator FadeInWinner(int runId)
    {
        const float revealDuration = 0.35f;
        var elapsed = 0f;

        while (elapsed < revealDuration && runId == _runId)
        {
            elapsed += Time.unscaledDeltaTime;
            _winnerCanvasGroup.alpha = Mathf.Clamp01(elapsed / revealDuration);
            yield return null;
        }

        if (runId == _runId)
        {
            _winnerCanvasGroup.alpha = 1f;

            // Commit the loaded sprite directly to the card after the cross-fade.
            // This avoids relying on a nested Image for the final frame.
            var winnerCard = _dummyCovers[WinnerIndex];
            winnerCard.sprite = _winnerCover.sprite;
            winnerCard.color = Color.white;
            _winnerCanvasGroup.alpha = 0f;
        }

        yield return new WaitForSecondsRealtime(0.45f);

        if (runId == _runId)
        {
            yield return TransitionToSongInfo(runId);
        }

        _revealCoroutine = null;
    }

    private IEnumerator TransitionToSongInfo(int runId)
    {
        var initialHeroSize = CardSize * (1f + MaximumCenterScaleBonus);
        _heroCover.sprite = _loadedCoverSprite ?? _roundedCardSprite;
        _heroCoverTransform.sizeDelta = new Vector2(initialHeroSize, initialHeroSize);
        _heroCoverTransform.anchoredPosition = Vector2.zero;
        _heroCoverCanvasGroup.alpha = 1f;
        _heroCoverTransform.gameObject.SetActive(true);

        const float rouletteFadeDuration = 0.45f;
        var elapsed = 0f;

        while (elapsed < rouletteFadeDuration && runId == _runId)
        {
            elapsed += Time.unscaledDeltaTime;
            var alpha = 1f - Mathf.Clamp01(elapsed / rouletteFadeDuration);
            _stripCanvasGroup.alpha = alpha;
            _centerMarkerCanvasGroup.alpha = alpha;
            var backgroundColor = _viewportBackgroundColor;
            backgroundColor.a *= alpha;
            _viewportBackground.color = backgroundColor;
            yield return null;
        }

        if (runId != _runId) yield break;

        _strip.gameObject.SetActive(false);
        _centerMarker.gameObject.SetActive(false);
        _viewportBackground.enabled = false;

        const float heroMoveDuration = 0.65f;
        var heroStartPosition = Vector2.zero;
        var heroTargetPosition = new Vector2(-ViewportWidth * 0.285f, 0f);
        var heroStartSize = new Vector2(initialHeroSize, initialHeroSize);
        var heroTargetSizeValue = CardSize * 1.48f;
        var heroTargetSize = new Vector2(heroTargetSizeValue, heroTargetSizeValue);
        elapsed = 0f;

        while (elapsed < heroMoveDuration && runId == _runId)
        {
            elapsed += Time.unscaledDeltaTime;
            var progress = Mathf.Clamp01(elapsed / heroMoveDuration);
            var eased = progress * progress * (3f - 2f * progress);
            _heroCoverTransform.anchoredPosition = Vector2.LerpUnclamped(
                heroStartPosition,
                heroTargetPosition,
                eased);
            _heroCoverTransform.sizeDelta = Vector2.LerpUnclamped(
                heroStartSize,
                heroTargetSize,
                eased);
            yield return null;
        }

        if (runId != _runId) yield break;

        _heroCoverTransform.anchoredPosition = heroTargetPosition;
        _heroCoverTransform.sizeDelta = heroTargetSize;
        var previewAvailable = !string.IsNullOrWhiteSpace(_previewUrl);
        _heroCoverButton.interactable = previewAvailable;
        _previewHoverHandler.enabled = previewAvailable;
        _previewOverlayCanvasGroup.alpha = 0f;
        _songDetails.gameObject.SetActive(true);
        _skipButton.interactable = true;
        _primaryButton.interactable = true;
        Canvas.ForceUpdateCanvases();
        EnforceSongDetailsBounds();
        LayoutRebuilder.ForceRebuildLayoutImmediate(_songDetails);
        EnforceSongDetailsBounds();
        var subNamePosition = _songSubName.anchoredPosition;
        subNamePosition.y = _songTitle.anchoredPosition.y + SubNameVerticalOffset;
        _songSubName.anchoredPosition = subNamePosition;

        foreach (var detailCanvasGroup in _songDetailCanvasGroups)
        {
            if (runId != _runId) yield break;
            if (!detailCanvasGroup.gameObject.activeInHierarchy) continue;

            var detailTransform = (RectTransform)detailCanvasGroup.transform;
            var targetPosition = detailTransform.anchoredPosition;
            var startPosition = targetPosition + new Vector2(0f, 2f);
            detailTransform.anchoredPosition = startPosition;
            detailCanvasGroup.alpha = 0f;
            elapsed = 0f;

            const float detailFadeDuration = 0.2f;
            while (elapsed < detailFadeDuration && runId == _runId)
            {
                elapsed += Time.unscaledDeltaTime;
                var progress = Mathf.Clamp01(elapsed / detailFadeDuration);
                detailCanvasGroup.alpha = progress;
                detailTransform.anchoredPosition = Vector2.LerpUnclamped(
                    startPosition,
                    targetPosition,
                    progress);
                yield return null;
            }

            detailCanvasGroup.alpha = 1f;
            detailTransform.anchoredPosition = targetPosition;
            yield return new WaitForSecondsRealtime(0.05f);
        }
    }

    private async void TogglePreviewPlayback()
    {
        if (_previewAudioSource.isPlaying)
        {
            _previewAudioSource.Pause();
            _previewPaused = true;
            RestoreMenuMusic();
            SetPreviewIcon(false);
            return;
        }

        if (_previewAudioClip == null)
        {
            var runId = _runId;
            _heroCoverButton.interactable = false;
            _previewIcon.text = "…";

            try
            {
                _previewAudioClip = await (_previewLoadTask ??= DownloadPreviewAsync(runId));
                _previewLoadTask = null;
            }
            catch (Exception exception)
            {
                _previewLoadTask = null;
                if (runId == _runId)
                {
                    Plugin.Log.Error($"Could not load the song preview: {exception}");
                    SetPreviewIcon(false);
                    _heroCoverButton.interactable = true;
                }
                return;
            }

            if (runId != _runId) return;

            _previewAudioSource.clip = _previewAudioClip;
            _heroCoverButton.interactable = true;
        }

        if (_previewPaused)
        {
            PauseMenuMusic();
            _previewAudioSource.UnPause();
        }
        else
        {
            PauseMenuMusic();
            _previewAudioSource.Play();
        }

        _previewPaused = false;
        SetPreviewIcon(true);

        if (_previewMonitorCoroutine != null)
        {
            StopCoroutine(_previewMonitorCoroutine);
        }
        _previewMonitorCoroutine = StartCoroutine(MonitorPreviewPlayback(_runId));
    }

    private async Task<AudioClip> DownloadPreviewAsync(int runId)
    {
        var previewUrl = _previewUrl
                         ?? throw new InvalidOperationException(
                             "BeatLeader did not provide a valid map hash for preview playback.");
        using var request = UnityWebRequestMultimedia.GetAudioClip(previewUrl, AudioType.MPEG);
        request.SetRequestHeader("User-Agent", "BeatLocator/0.0.1");
        var operation = request.SendWebRequest();

        while (!operation.isDone)
        {
            if (runId != _runId)
            {
                request.Abort();
                throw new TaskCanceledException("The preview request was cancelled.");
            }

            await Task.Delay(20);
        }

        if (request.result != UnityWebRequest.Result.Success)
        {
            throw new InvalidOperationException(
                $"Preview request failed: {request.responseCode} {request.error}");
        }

        return DownloadHandlerAudioClip.GetContent(request)
               ?? throw new InvalidOperationException("The preview request returned no audio clip.");
    }

    private IEnumerator MonitorPreviewPlayback(int runId)
    {
        while (runId == _runId && _previewAudioSource.isPlaying)
        {
            yield return null;
        }

        if (runId == _runId && !_previewPaused)
        {
            RestoreMenuMusic();
            SetPreviewIcon(false);
        }

        _previewMonitorCoroutine = null;
    }

    private void StopPreviewPlayback()
    {
        if (_previewMonitorCoroutine != null)
        {
            StopCoroutine(_previewMonitorCoroutine);
            _previewMonitorCoroutine = null;
        }

        if (_previewAudioSource != null)
        {
            _previewAudioSource.Stop();
            _previewAudioSource.clip = _previewAudioClip;
        }

        _previewPaused = false;
        RestoreMenuMusic();
        if (_previewIcon != null)
        {
            SetPreviewIcon(false);
        }
    }

    private void SetPreviewIcon(bool isPlaying)
    {
        _previewIcon.text = isPlaying ? "Ⅱ" : "▶";
    }

    private void OnPreviewHoverChanged(bool isHovered)
    {
        if (_heroCover == null || _previewOverlayCanvasGroup == null) return;

        _heroCover.color = isHovered
            ? new Color(0.55f, 0.55f, 0.55f, 1f)
            : Color.white;
        _previewOverlayCanvasGroup.alpha = isHovered ? 1f : 0f;
    }

    private void PauseMenuMusic()
    {
        if (_menuMusicPausedByPreview) return;

        _songPreviewPlayer.PauseCurrentChannel();
        _menuMusicPausedByPreview = true;
    }

    private void RestoreMenuMusic()
    {
        if (!_menuMusicPausedByPreview) return;

        _songPreviewPlayer.UnPauseCurrentChannel();
        _menuMusicPausedByPreview = false;
    }

    private void SetPrimaryButtonText(string text)
    {
        PrimaryButtonText = text;
        NotifyPropertyChanged(nameof(PrimaryButtonText));
    }

    private static string ValueOrFallback(string? value, string fallback)
    {
        if (value == null || value.Trim().Length == 0)
        {
            return fallback;
        }

        return value;
    }

    private static string FormatDifficulty(EvaluatedDifficulty selectedDifficulty)
    {
        var difficultyName = ValueOrFallback(
            selectedDifficulty.Difficulty.DifficultyName,
            "Unknown");
        var difficultyLabel = difficultyName.Equals(
            "ExpertPlus",
            StringComparison.OrdinalIgnoreCase)
            ? "EXPERT+"
            : difficultyName.ToUpperInvariant();
        var mode = selectedDifficulty.Difficulty.ModeName;
        if (mode != null && mode.Trim().Length > 0 &&
            !mode.Equals("Standard", StringComparison.OrdinalIgnoreCase))
        {
            difficultyLabel += $" ({mode})";
        }

        var stars = selectedDifficulty.Difficulty.Stars.HasValue
            ? selectedDifficulty.Difficulty.Stars.Value.ToString("0.00")
            : "?";
        return $"{difficultyLabel}  •  {stars} ★";
    }

    private void SelectCoverUrls(string? fullCoverUrl, string? coverUrl)
    {
        _coverUrl = null;
        _fallbackCoverUrl = null;

        // Unity's built-in texture decoder does not support the WebP images used
        // by some BeatLeader fullCoverImage values. Prefer the API's JPEG cover
        // for those maps; for decodable full covers, keep the JPEG as a fallback.
        if (fullCoverUrl != null && fullCoverUrl.Trim().Length > 0)
        {
            if (IsWebpUrl(fullCoverUrl) && coverUrl != null && coverUrl.Trim().Length > 0)
            {
                _coverUrl = coverUrl;
                Plugin.Log.Info("Using the JPEG cover because the full cover is WebP.");
                return;
            }

            _coverUrl = fullCoverUrl;
            if (coverUrl != null && coverUrl.Trim().Length > 0 &&
                !string.Equals(fullCoverUrl, coverUrl, StringComparison.OrdinalIgnoreCase))
            {
                _fallbackCoverUrl = coverUrl;
            }
            return;
        }

        if (coverUrl != null && coverUrl.Trim().Length > 0)
        {
            _coverUrl = coverUrl;
        }
    }

    private static bool IsWebpUrl(string coverUrl)
    {
        var queryIndex = coverUrl.IndexOf('?');
        var path = queryIndex >= 0 ? coverUrl.Substring(0, queryIndex) : coverUrl;
        return path.EndsWith(".webp", StringComparison.OrdinalIgnoreCase);
    }

    private static string? BuildPreviewUrl(string? hash)
    {
        if (hash == null || hash.Trim().Length == 0 || hash.Length != 40 ||
            hash.Any(character => !Uri.IsHexDigit(character)))
        {
            return null;
        }

        return PreviewCdnBaseUrl + hash.ToLowerInvariant() + ".mp3";
    }

    private void ReleaseSelectedMedia()
    {
        StopPreviewPlayback();
        StopAnimations();
        _coverLoadTask = null;
        _previewLoadTask = null;

        if (_previewAudioSource != null)
        {
            _previewAudioSource.clip = null;
        }

        if (_loadedCoverSprite != null)
        {
            Destroy(_loadedCoverSprite);
            _loadedCoverSprite = null;
        }

        if (_loadedCoverTexture != null)
        {
            Destroy(_loadedCoverTexture);
            _loadedCoverTexture = null;
        }

        if (_previewAudioClip != null)
        {
            Destroy(_previewAudioClip);
            _previewAudioClip = null;
        }
    }

    private void StopAnimations()
    {
        if (_spinCoroutine != null)
        {
            StopCoroutine(_spinCoroutine);
            _spinCoroutine = null;
        }

        if (_revealCoroutine != null)
        {
            StopCoroutine(_revealCoroutine);
            _revealCoroutine = null;
        }
    }

    private void SetStatus(string status)
    {
        StatusText = status;
        NotifyPropertyChanged(nameof(StatusText));
    }

    protected override void OnDestroy()
    {
        if (_loadedCoverSprite != null)
        {
            Destroy(_loadedCoverSprite);
        }

        if (_loadedCoverTexture != null)
        {
            Destroy(_loadedCoverTexture);
        }

        if (_roundedCardSprite != null)
        {
            Destroy(_roundedCardSprite);
        }

        if (_roundedCardTexture != null)
        {
            Destroy(_roundedCardTexture);
        }

        if (_previewAudioClip != null)
        {
            Destroy(_previewAudioClip);
        }

        base.OnDestroy();
    }
}
