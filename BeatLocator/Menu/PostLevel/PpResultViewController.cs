using BeatLocator.PostLevel;
using BeatLocator.EvaluationManagers;
using BeatSaberMarkupLanguage.Attributes;
using BeatSaberMarkupLanguage.ViewControllers;
using System.Collections;
using System.Globalization;
using TMPro;
using UnityEngine;
using Zenject;

namespace BeatLocator.Menu;

internal sealed class PpResultViewController : BSMLAutomaticViewController
{
    private const string PpTickResource = "BeatLocator.Assets.pp_tick.wav";

    private const float CountUpDurationSeconds = 1.6f;
    private const float ButtonFadeDurationSeconds = 0.3f;
    private const int PpTickEveryNumericChange = 8;
    private const float PpTickStartPitch = 0.85f;
    private const float PpTickEndPitch = 1.65f;
    private const float PpTickVolume = 0.4f;

    [UIComponent("pp-root")]
    private RectTransform _ppRoot = null!;

    [UIComponent("primary-value")]
    private TMP_Text _primaryValue = null!;

    [UIComponent("button-row")]
    private RectTransform _buttonRow = null!;

    [UIValue("primaryText")]
    public string PrimaryText { get; private set; } = "0.00pp";

    [UIValue("secondaryText")]
    public string SecondaryText { get; private set; } = "(+0.00pp)";

    private BeatLocatorFlowCoordinator _flowCoordinator = null!;
    private PostLevelDisplayResult? _result;
    private Coroutine? _animationCoroutine;
    private int _animationId;
    private CanvasGroup? _buttonCanvasGroup;
    private AudioSource? _ppTickAudioSource;
    private AudioClip? _ppTickAudioClip;
    private int _numericChangeCount;

    [Inject]
    private void Construct(BeatLocatorFlowCoordinator flowCoordinator)
    {
        _flowCoordinator = flowCoordinator;
    }

    internal void SetResult(PostLevelDisplayResult result)
    {
        _result = result;
        _animationId++;
        PrimaryText = result.Outcome == PpResolutionOutcome.UploadedNewBest
            ? "0.00pp"
            : GetStatusText(result.Outcome);
        SecondaryText = result.Outcome == PpResolutionOutcome.UploadedNewBest
            ? "(+0.00pp)"
            : GetStatusDetail(result);
        NotifyPropertyChanged(nameof(PrimaryText));
        NotifyPropertyChanged(nameof(SecondaryText));
        SetButtonsVisible(false);
    }

    protected override void DidActivate(
        bool firstActivation,
        bool addedToHierarchy,
        bool screenSystemEnabling)
    {
        base.DidActivate(firstActivation, addedToHierarchy, screenSystemEnabling);
        _ppRoot.gameObject.SetActive(true);
        if ((!addedToHierarchy && !screenSystemEnabling) || _result == null) return;

        _primaryValue.fontSize = _result.Outcome == PpResolutionOutcome.UploadedNewBest
            ? 18f
            : 7f;
        _buttonCanvasGroup ??= _buttonRow.GetComponent<CanvasGroup>() ??
                               _buttonRow.gameObject.AddComponent<CanvasGroup>();
        EnsurePpTickAudio();
        _numericChangeCount = 0;
        SetButtonsVisible(false);
        if (_animationCoroutine != null)
        {
            StopCoroutine(_animationCoroutine);
        }
        _animationCoroutine = StartCoroutine(AnimateResult(_animationId, _result));
    }

    protected override void DidDeactivate(
        bool removedFromHierarchy,
        bool screenSystemDisabling)
    {
        if (_animationCoroutine != null)
        {
            StopCoroutine(_animationCoroutine);
            _animationCoroutine = null;
        }
        _ppTickAudioSource?.Stop();
        if (removedFromHierarchy || screenSystemDisabling)
        {
            _ppRoot.gameObject.SetActive(false);
        }
        base.DidDeactivate(removedFromHierarchy, screenSystemDisabling);
    }

    private IEnumerator AnimateResult(int animationId, PostLevelDisplayResult result)
    {
        if (result.Outcome == PpResolutionOutcome.UploadedNewBest)
        {
            var targetScorePp = result.ScorePp ?? 0d;
            var targetProfileGain = result.ProfileGain ?? 0d;
            var elapsed = 0f;
            while (elapsed < CountUpDurationSeconds && animationId == _animationId)
            {
                elapsed += Time.unscaledDeltaTime;
                var progress = Mathf.Clamp01(elapsed / CountUpDurationSeconds);
                var eased = 1f - Mathf.Pow(1f - progress, 3f);
                if (SetNumericText(targetScorePp * eased, targetProfileGain * eased))
                {
                    PlayPpTick(progress);
                }
                yield return null;
            }

            if (animationId != _animationId) yield break;
            if (SetNumericText(targetScorePp, targetProfileGain))
            {
                PlayPpTick(1f);
            }
        }
        else
        {
            yield return new WaitForSecondsRealtime(0.65f);
            if (animationId != _animationId) yield break;
        }

        yield return FadeInButtons(animationId);
        _animationCoroutine = null;
    }

    private IEnumerator FadeInButtons(int animationId)
    {
        if (_buttonCanvasGroup == null) yield break;

        _buttonCanvasGroup.alpha = 0f;
        _buttonCanvasGroup.interactable = false;
        _buttonCanvasGroup.blocksRaycasts = false;
        var elapsed = 0f;
        while (elapsed < ButtonFadeDurationSeconds && animationId == _animationId)
        {
            elapsed += Time.unscaledDeltaTime;
            var progress = Mathf.Clamp01(elapsed / ButtonFadeDurationSeconds);
            _buttonCanvasGroup.alpha = 1f - Mathf.Pow(1f - progress, 2f);
            yield return null;
        }

        if (animationId != _animationId) yield break;
        SetButtonsVisible(true);
    }

    private bool SetNumericText(double scorePp, double profileGain)
    {
        var primaryText = scorePp.ToString("0.00", CultureInfo.InvariantCulture) + "pp";
        var secondaryText = "(" +
                            profileGain.ToString(
                                "+0.00;-0.00;+0.00",
                                CultureInfo.InvariantCulture) +
                            "pp)";
        var changed = primaryText != PrimaryText || secondaryText != SecondaryText;
        if (!changed) return false;

        PrimaryText = primaryText;
        SecondaryText = secondaryText;
        NotifyPropertyChanged(nameof(PrimaryText));
        NotifyPropertyChanged(nameof(SecondaryText));
        return true;
    }

    private void EnsurePpTickAudio()
    {
        if (_ppTickAudioSource == null)
        {
            _ppTickAudioSource = gameObject.AddComponent<AudioSource>();
            _ppTickAudioSource.playOnAwake = false;
            _ppTickAudioSource.loop = false;
            _ppTickAudioSource.spatialBlend = 0f;
        }

        if (_ppTickAudioClip != null) return;

        try
        {
            _ppTickAudioClip = RouletteAnimationViewController.LoadPcmWave(
                PpTickResource,
                "BeatLocator PP Tick");
        }
        catch (System.Exception exception)
        {
            Plugin.Log.Error($"[PP UI] Could not load PP tick sound: {exception}");
        }
    }

    private void PlayPpTick(float progress)
    {
        if (_ppTickAudioSource == null || _ppTickAudioClip == null) return;

        _numericChangeCount++;
        if (_numericChangeCount % PpTickEveryNumericChange != 0) return;

        _ppTickAudioSource.pitch = Mathf.Lerp(
            PpTickStartPitch,
            PpTickEndPitch,
            Mathf.Clamp01(progress));
        _ppTickAudioSource.PlayOneShot(_ppTickAudioClip, PpTickVolume);
    }

    private void SetButtonsVisible(bool visible)
    {
        if (_buttonCanvasGroup == null) return;

        _buttonCanvasGroup.alpha = visible ? 1f : 0f;
        _buttonCanvasGroup.interactable = visible;
        _buttonCanvasGroup.blocksRaycasts = visible;
    }

    [UIAction("change-settings")]
    private void ChangeSettingsPressed()
    {
        SetButtonsVisible(false);
        _flowCoordinator.ShowRankingSelect();
    }

    [UIAction("next")]
    private void NextPressed()
    {
        SetButtonsVisible(false);
        _flowCoordinator.StartNextRoulette();
    }

    private static string GetStatusText(PpResolutionOutcome outcome)
    {
        return outcome == PpResolutionOutcome.NonPersonalBest
            ? "PERSONAL BEST STANDS"
            : "PP UNAVAILABLE";
    }

    private static string GetStatusDetail(PostLevelDisplayResult result)
    {
        if (result.Outcome == PpResolutionOutcome.NonPersonalBest)
        {
            return $"{result.LocalScore.ToString("N0", CultureInfo.InvariantCulture)}  •  " +
                   $"{result.LocalRank}  •  {result.LocalMaxCombo} COMBO";
        }

        return result.Provider.GetDisplayName();
    }

    protected override void OnDestroy()
    {
        if (_ppTickAudioClip != null)
        {
            Destroy(_ppTickAudioClip);
            _ppTickAudioClip = null;
        }

        base.OnDestroy();
    }
}
