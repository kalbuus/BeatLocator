using System.Collections;
using System.Collections.Generic;
using BeatLocator.Settings;
using BeatSaberMarkupLanguage.Attributes;
using BeatSaberMarkupLanguage.ViewControllers;
using HMUI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Zenject;

namespace BeatLocator.Menu;

/// <summary>Experimental BeatSaver settings using the original text-segment UI.</summary>
public sealed class BeatSaverSelect : BSMLAutomaticViewController
{
    [UIComponent("beatsaver-root")] private RectTransform _root = null!;
    [UIComponent("find-button")] private Button _findButton = null!;
    [UIComponent("standard-button")] private Button _standardButton = null!;
    [UIComponent("standard-label")] private TMP_Text _standardLabel = null!;
    [UIComponent("secret-button")] private Button _secretButton = null!;
    [UIComponent("secret-label")] private TMP_Text _secretLabel = null!;
    [UIComponent("nps-segments")] private RectTransform _npsSegments = null!;
    [UIComponent("nps-slider")] private Image _npsSliderImage = null!;
    private SegmentSelectionSlider? _npsSlider;
    private int _nps;
    [UIComponent("njs-segments")] private RectTransform _njsSegments = null!;
    [UIComponent("njs-slider")] private Image _njsSliderImage = null!;
    private SegmentSelectionSlider? _njsSlider;
    private int _njs;
    [UIComponent("style-segments")] private RectTransform _styleSegments = null!;
    [UIComponent("style-slider")] private Image _styleSliderImage = null!;
    private SegmentSelectionSlider? _styleSlider;
    private int _style;
    [UIComponent("duration-segments")] private RectTransform _durationSegments = null!;
    [UIComponent("duration-slider")] private Image _durationSliderImage = null!;
    private SegmentSelectionSlider? _durationSlider;
    private int _duration;
    [UIComponent("quality-segments")] private RectTransform _qualitySegments = null!;
    [UIComponent("quality-slider")] private Image _qualitySliderImage = null!;
    private SegmentSelectionSlider? _qualitySlider;
    private int _quality;

    private BeatLocatorFlowCoordinator _flow = null!;
    private PluginConfig _config = null!;
    private ToggleButtonVisual? _standardVisual;
    private ToggleButtonVisual? _secretVisual;
    private bool _prepared;
    private BeatSaverSearchRequest? _lastRequest;

    [UIValue("findButtonText")] public string FindButtonText { get; private set; } = "<b>FIND SOME MAPS!</b>";
    [UIValue("searchInProgress")] public bool SearchInProgress { get; private set; }
    [UIValue("npsOptions")] public List<string> NpsOptions { get; } = new List<string>(BeatSaverSearchRequest.NpsLabels);
    [UIValue("njsOptions")] public List<string> NjsOptions { get; } = new List<string>(BeatSaverSearchRequest.NjsLabels);
    [UIValue("styleOptions")] public List<string> StyleOptions { get; } = new List<string>(BeatSaverSearchRequest.StyleLabels);
    [UIValue("durationOptions")] public List<string> DurationOptions { get; } = RankingSelectViewSupport.CreateDurations();
    [UIValue("qualityOptions")] public List<string> QualityOptions { get; } = new List<string>(BeatSaverSearchRequest.QualityLabels);

    [Inject]
    private void Construct(BeatLocatorFlowCoordinator flow, PluginConfig config)
    {
        _flow = flow;
        _config = config;
        var request = new BeatSaverSearchRequest(config.BeatSaverNpsSelection, config.BeatSaverNjsSelection,
            config.BeatSaverStyleSelection, config.BeatSaverDurationSelection,
            config.BeatSaverQualitySelection, config.BeatSaverStandardOnly, config.BeatSaverSecretDifficulty);
        _nps = config.BeatSaverNpsSelection = request.NpsSelection;
        _njs = config.BeatSaverNjsSelection = request.NjsSelection;
        _style = config.BeatSaverStyleSelection = request.StyleSelection;
        _duration = config.BeatSaverDurationSelection = (int)request.Duration;
        _quality = config.BeatSaverQualitySelection = request.QualitySelection;
    }

    internal bool PrepareForFirstPresentation()
    {
        if (_prepared) return false;
        ParseWithFallback();
        return _prepared;
    }

    [UIAction("#post-parse")]
    private void PostParse()
    {
        var sprite = RankingSelectViewSupport.LoadSprite(RankingSelectViewSupport.NineSliceResource);
        _npsSlider = InitializeSlider(_npsSegments, _npsSliderImage, sprite, NpsOptions.Count, _nps);
        _njsSlider = InitializeSlider(_njsSegments, _njsSliderImage, sprite, NjsOptions.Count, _njs);
        _styleSlider = InitializeSlider(_styleSegments, _styleSliderImage, sprite, StyleOptions.Count, _style);
        _durationSlider = InitializeSlider(_durationSegments, _durationSliderImage, sprite, DurationOptions.Count, _duration);
        _qualitySlider = InitializeSlider(_qualitySegments, _qualitySliderImage, sprite, QualityOptions.Count, _quality);
        _standardVisual = RankingSelectViewSupport.InitializeToggleVisual(
            _standardButton, _standardLabel, _config.BeatSaverStandardOnly, sprite, _npsSliderImage);
        _secretVisual = RankingSelectViewSupport.InitializeToggleVisual(
            _secretButton, _secretLabel, _config.BeatSaverSecretDifficulty, sprite, _npsSliderImage);
        _prepared = true;
        SetSearchInProgress(SearchInProgress);
        RankingSelectViewSupport.ForceLayout(_root);
    }

    private static SegmentSelectionSlider InitializeSlider(RectTransform track, Image image,
        Sprite sprite, int count, int selection)
    {
        image.sprite = sprite;
        var slider = image.gameObject.AddComponent<SegmentSelectionSlider>();
        slider.Initialize(track, image, count, selection);
        track.GetComponent<SegmentedControl>()?.SelectCellWithNumber(selection);
        return slider;
    }

    [UIAction("npsSelected")]
    private void NpsSelected(object control, int index)
    {
        _nps = RankingSelectViewSupport.NormalizeSelection(index, NpsOptions.Count);
        _config.BeatSaverNpsSelection = _nps;
        _npsSlider?.MoveTo(_nps);
    }

    [UIAction("njsSelected")]
    private void NjsSelected(object control, int index)
    {
        _njs = RankingSelectViewSupport.NormalizeSelection(index, NjsOptions.Count);
        _config.BeatSaverNjsSelection = _njs;
        _njsSlider?.MoveTo(_njs);
    }

    [UIAction("styleSelected")]
    private void StyleSelected(object control, int index)
    {
        _style = RankingSelectViewSupport.NormalizeSelection(index, StyleOptions.Count);
        _config.BeatSaverStyleSelection = _style;
        _styleSlider?.MoveTo(_style);
    }

    [UIAction("durationSelected")]
    private void DurationSelected(object control, int index)
    {
        _duration = RankingSelectViewSupport.NormalizeSelection(index, DurationOptions.Count);
        _config.BeatSaverDurationSelection = _duration;
        _durationSlider?.MoveTo(_duration);
    }

    [UIAction("qualitySelected")]
    private void QualitySelected(object control, int index)
    {
        _quality = RankingSelectViewSupport.NormalizeSelection(index, QualityOptions.Count);
        _config.BeatSaverQualitySelection = _quality;
        _qualitySlider?.MoveTo(_quality);
    }

    [UIAction("toggleStandard")]
    private void ToggleStandard()
    {
        _config.BeatSaverStandardOnly = !_config.BeatSaverStandardOnly;
        _standardVisual?.SetActive(_config.BeatSaverStandardOnly);
    }

    [UIAction("toggleSecret")]
    private void ToggleSecret()
    {
        _config.BeatSaverSecretDifficulty = !_config.BeatSaverSecretDifficulty;
        _secretVisual?.SetActive(_config.BeatSaverSecretDifficulty);
    }

    [UIAction("findPressed")]
    private void FindPressed()
    {
        if (SearchInProgress) return;
        _lastRequest = new BeatSaverSearchRequest(_nps, _njs, _style, _duration, _quality,
            _config.BeatSaverStandardOnly, _config.BeatSaverSecretDifficulty);
        StartSearch(_lastRequest);
    }

    internal void RepeatLastSearch()
    {
        if (SearchInProgress) return;
        if (_lastRequest == null) FindPressed();
        else StartSearch(_lastRequest);
    }

    private void StartSearch(BeatSaverSearchRequest request)
    {
        SetSearchInProgress(true);
        _flow.FindBeatSaverMapAsync(request);
    }

    internal void SetSearchInProgress(bool active)
    {
        SearchInProgress = active;
        FindButtonText = active ? "<b>LOADING...</b>" : "<b>FIND SOME MAPS!</b>";
        NotifyPropertyChanged(nameof(SearchInProgress));
        NotifyPropertyChanged(nameof(FindButtonText));
        if (_findButton) _findButton.interactable = !active;
        if (_root)
        {
            foreach (var control in _root.GetComponentsInChildren<SegmentedControl>(true))
            {
                var group = control.GetComponent<CanvasGroup>() ?? control.gameObject.AddComponent<CanvasGroup>();
                group.interactable = !active;
                group.blocksRaycasts = !active;
            }
        }
        if (_standardButton) _standardButton.interactable = !active;
        if (_secretButton) _secretButton.interactable = !active;
    }

    [UIAction("exitPressed")] private void ExitPressed() => _flow.ShowSelect();
    [UIAction("gameplay-settings")]
    private void GameplaySettingsPressed() => _flow.ToggleGameplaySettings();

    [UIAction("cancelLoading")] private void CancelLoading() => _flow.CancelLoading();

    protected override void DidActivate(bool firstActivation, bool addedToHierarchy, bool screenSystemEnabling)
    {
        base.DidActivate(firstActivation && !_prepared, addedToHierarchy, screenSystemEnabling);
        _root.anchorMin = _root.anchorMax = _root.pivot = new Vector2(0.5f, 0.5f);
        _root.anchoredPosition = Vector2.zero;
        _root.sizeDelta = new Vector2(112f, 100f);
        SetSearchInProgress(SearchInProgress);
        RankingSelectViewSupport.ForceLayout(_root);
        StartCoroutine(RebuildNextFrame());
    }

    private IEnumerator RebuildNextFrame()
    {
        yield return null;
        RankingSelectViewSupport.ForceLayout(_root);
    }
}
