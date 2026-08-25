using BeatSaberMarkupLanguage.Attributes;
using BeatSaberMarkupLanguage.ViewControllers;
using UnityEngine;
using Zenject;

namespace BeatLocator.Menu;

internal sealed class PostLevelTerminalViewController : BSMLAutomaticViewController
{
    [UIComponent("terminal-root")]
    private RectTransform _terminalRoot = null!;

    [UIValue("statusText")]
    public string StatusText { get; private set; } = string.Empty;

    [UIComponent("button-row")]
    private RectTransform _buttonRow = null!;

    private BeatLocatorFlowCoordinator _flowCoordinator = null!;
    private CanvasGroup? _buttonCanvasGroup;

    [Inject]
    private void Construct(BeatLocatorFlowCoordinator flowCoordinator)
    {
        _flowCoordinator = flowCoordinator;
    }

    internal void SetLevelFailed(bool levelFailed)
    {
        StatusText = levelFailed ? "LEVEL FAILED" : string.Empty;
        NotifyPropertyChanged(nameof(StatusText));
        SetButtonsInteractable(true);
    }

    protected override void DidActivate(
        bool firstActivation,
        bool addedToHierarchy,
        bool screenSystemEnabling)
    {
        base.DidActivate(firstActivation, addedToHierarchy, screenSystemEnabling);
        _terminalRoot.gameObject.SetActive(true);

        _buttonCanvasGroup ??= _buttonRow.GetComponent<CanvasGroup>() ??
                               _buttonRow.gameObject.AddComponent<CanvasGroup>();
        SetButtonsInteractable(true);
    }

    protected override void DidDeactivate(
        bool removedFromHierarchy,
        bool screenSystemDisabling)
    {
        SetButtonsInteractable(false);
        if (removedFromHierarchy || screenSystemDisabling)
        {
            _terminalRoot.gameObject.SetActive(false);
        }
        base.DidDeactivate(removedFromHierarchy, screenSystemDisabling);
    }

    private void SetButtonsInteractable(bool interactable)
    {
        if (_buttonCanvasGroup == null) return;
        _buttonCanvasGroup.interactable = interactable;
        _buttonCanvasGroup.blocksRaycasts = interactable;
    }

    [UIAction("menu")]
    private void MenuPressed()
    {
        SetButtonsInteractable(false);
        _flowCoordinator.ShowRankingSelect();
    }

    [UIAction("retry")]
    private void RetryPressed()
    {
        SetButtonsInteractable(false);
        _flowCoordinator.RetryPostLevelMap();
    }

    [UIAction("skip")]
    private void SkipPressed()
    {
        SetButtonsInteractable(false);
        _flowCoordinator.StartNextRoulette();
    }
}
