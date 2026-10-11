using BeatSaberMarkupLanguage.Attributes;
using BeatSaberMarkupLanguage.ViewControllers;
using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using Zenject;

namespace BeatLocator.Menu;

internal sealed class PostLevelLoadingViewController : BSMLAutomaticViewController
{
    [UIComponent("loading-root")]
    private RectTransform _loadingRoot = null!;
    [UIComponent("cancel-button")]
    private RectTransform _cancelButton = null!;

    [UIValue("loadingText")]
    public string LoadingText { get; private set; } = "CALCULATING PP";

    [UIValue("dotsText")]
    public string DotsText { get; private set; } = "•";

    private Coroutine? _animationCoroutine;
    private int _animationId;
    private BeatLocatorFlowCoordinator _flowCoordinator = null!;

    [Inject]
    private void Construct(BeatLocatorFlowCoordinator flowCoordinator)
    {
        _flowCoordinator = flowCoordinator;
    }

    [UIAction("#post-parse")]
    private void ConfigureCancelButton()
    {
        // BSML's ButtonTag adds a ContentSizeFitter to the Practice-button
        // prefab. It can override the requested dimensions after a layout pass.
        foreach (var fitter in _cancelButton.GetComponents<ContentSizeFitter>())
            fitter.enabled = false;
        var element = _cancelButton.GetComponent<LayoutElement>();
        if (element != null) element.ignoreLayout = true;
        _cancelButton.anchorMin = _cancelButton.anchorMax = new Vector2(0.5f, 0.5f);
        _cancelButton.anchoredPosition = new Vector2(0f, -11f);
        _cancelButton.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, 24f);
        _cancelButton.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, 8f);
    }

    [UIAction("cancel")]
    private void CancelPressed()
    {
        _flowCoordinator.CancelLoading();
    }

    internal void SetMessage(string message)
    {
        LoadingText = message;
        NotifyPropertyChanged(nameof(LoadingText));
    }

    protected override void DidActivate(
        bool firstActivation,
        bool addedToHierarchy,
        bool screenSystemEnabling)
    {
        base.DidActivate(firstActivation, addedToHierarchy, screenSystemEnabling);
        _loadingRoot.gameObject.SetActive(true);
        ConfigureCancelButton();
        if (!addedToHierarchy && !screenSystemEnabling) return;

        _animationId++;
        if (_animationCoroutine != null)
        {
            StopCoroutine(_animationCoroutine);
        }
        _animationCoroutine = StartCoroutine(AnimateDots(_animationId));
        StartCoroutine(LogCancelLayoutNextFrame(_animationId));
    }

    private IEnumerator LogCancelLayoutNextFrame(int animationId)
    {
        yield return null;
        if (animationId != _animationId || !isActivated) yield break;
        var fitter = _cancelButton.GetComponent<ContentSizeFitter>();
        Plugin.Log.Info(
            $"[UI] Loading Cancel: size={_cancelButton.rect.size}, " +
            $"position={_cancelButton.anchoredPosition}, autoSize={fitter != null && fitter.enabled}.");
    }

    protected override void DidDeactivate(
        bool removedFromHierarchy,
        bool screenSystemDisabling)
    {
        _animationId++;
        if (_animationCoroutine != null)
        {
            StopCoroutine(_animationCoroutine);
            _animationCoroutine = null;
        }
        if (removedFromHierarchy || screenSystemDisabling)
        {
            _loadingRoot.gameObject.SetActive(false);
        }
        base.DidDeactivate(removedFromHierarchy, screenSystemDisabling);
    }

    private IEnumerator AnimateDots(int animationId)
    {
        var count = 1;
        while (animationId == _animationId)
        {
            DotsText = new string('•', count);
            NotifyPropertyChanged(nameof(DotsText));
            count = count % 3 + 1;
            yield return new WaitForSecondsRealtime(0.35f);
        }
    }
}
