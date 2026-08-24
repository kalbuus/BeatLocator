using BeatSaberMarkupLanguage.Attributes;
using BeatSaberMarkupLanguage.ViewControllers;
using System.Collections;
using UnityEngine;

namespace BeatLocator.Menu;

internal sealed class PostLevelLoadingViewController : BSMLAutomaticViewController
{
    [UIValue("loadingText")]
    public string LoadingText { get; private set; } = "CALCULATING PP";

    [UIValue("dotsText")]
    public string DotsText { get; private set; } = "•";

    private Coroutine? _animationCoroutine;
    private int _animationId;

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
        if (!addedToHierarchy) return;

        _animationId++;
        if (_animationCoroutine != null)
        {
            StopCoroutine(_animationCoroutine);
        }
        _animationCoroutine = StartCoroutine(AnimateDots(_animationId));
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
