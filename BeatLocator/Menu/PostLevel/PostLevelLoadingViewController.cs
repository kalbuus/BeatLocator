using BeatSaberMarkupLanguage.Attributes;
using BeatSaberMarkupLanguage.ViewControllers;
using MotionUtils;
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

    private int _animationId;
    private MotionScope? _motion;
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
        _motion ??= MotionUtils.Motion.For(this);
        StartDotsSequence(_animationId);
    }

    protected override void DidDeactivate(
        bool removedFromHierarchy,
        bool screenSystemDisabling)
    {
        _animationId++;
        _motion?.Kill("loading-dots");
        if (removedFromHierarchy || screenSystemDisabling)
        {
            _loadingRoot.gameObject.SetActive(false);
        }
        base.DidDeactivate(removedFromHierarchy, screenSystemDisabling);
    }

    private void StartDotsSequence(int animationId)
    {
        if (_motion == null || animationId != _animationId)
        {
            return;
        }

        _motion.Sequence("loading-dots")
            .At(0f, 0f, _ => SetDots(1), EaseType.Linear)
            .At(0.35f, 0f, _ => SetDots(2), EaseType.Linear)
            .At(0.7f, 0f, _ => SetDots(3), EaseType.Linear)
            .AppendDelay(0.35f)
            .OnCompleted(() => StartDotsSequence(animationId))
            .Play();
    }

    private void SetDots(int count)
    {
        DotsText = new string('•', count);
        NotifyPropertyChanged(nameof(DotsText));
    }
}
