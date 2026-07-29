using BeatSaberMarkupLanguage.Attributes;
using BeatSaberMarkupLanguage.Components;
using BeatSaberMarkupLanguage.ViewControllers;
using HMUI;
using IPA.Loader;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Zenject;

namespace BeatLocator.Menu;

public class SelectViewController : BSMLAutomaticViewController
{
    private const string FeedbackUrl =
        "https://docs.google.com/forms/d/e/1FAIpQLScuMAm1XBrDSmW3VGs5oYQw_WkMC3xXz_o9N_OV9fYRN0LJzQ/viewform?usp=sharing&ouid=110581073513055024563";

    [UIComponent("beatsaver-button")]
    private Button _beatSaverButton = null!;
    [UIComponent("beatsaver-background")]
    private Image _beatSaverBackground = null!;
    [UIComponent("beatsaver-logo")]
    private Image _beatSaverLogo = null!;
    [UIComponent("beatsaver-label")]
    private TMP_Text _beatSaverLabel = null!;
    [UIComponent("beatleader-button")]
    private Button _beatLeaderButton = null!;
    [UIComponent("beatleader-background")]
    private Image _beatLeaderBackground = null!;
    [UIComponent("beatleader-logo")]
    private Image _beatLeaderLogo = null!;
    [UIComponent("beatleader-label")]
    private TMP_Text _beatLeaderLabel = null!;
    [UIComponent("scoresaber-button")]
    private Button _scoreSaberButton = null!;
    [UIComponent("scoresaber-background")]
    private Image _scoreSaberBackground = null!;
    [UIComponent("scoresaber-logo")]
    private Image _scoreSaberLogo = null!;
    [UIComponent("scoresaber-label")]
    private TMP_Text _scoreSaberLabel = null!;

    private bool IsBeatLeaderInstalled => PluginManager.GetPluginFromId("BeatLeader") != null;
    [UIValue("beatLeaderButtonHover")]
    public string BeatLeaderButtonHover => IsBeatLeaderInstalled ? "Play BL's Rating Maps Based On Your Skills" : "BeatLeader Mod Is Not Installed";

    private BeatLocatorFlowCoordinator _flowCoordinator = null!;

    [Inject]
    private void Construct(BeatLocatorFlowCoordinator flowCoordinator)
    {
        _flowCoordinator = flowCoordinator;
    }

    [UIAction("#post-parse")]
    private void PostParse()
    {
        _beatLeaderButton.interactable = IsBeatLeaderInstalled;

        RemoveNativeButtonBackground(
            _beatSaverButton,
            _beatSaverBackground,
            _beatSaverLogo,
            _beatSaverLabel);
        RemoveNativeButtonBackground(
            _beatLeaderButton,
            _beatLeaderBackground,
            _beatLeaderLogo,
            _beatLeaderLabel);
        RemoveNativeButtonBackground(
            _scoreSaberButton,
            _scoreSaberBackground,
            _scoreSaberLogo,
            _scoreSaberLabel);
    }

    private static void RemoveNativeButtonBackground(
        Button button,
        Image customBackground,
        Image customLogo,
        TMP_Text customLabel)
    {
        button.transition = Selectable.Transition.None;

        foreach (var staticAnimations in
                 button.GetComponentsInChildren<ButtonStaticAnimations>(true))
        {
            staticAnimations.enabled = false;
        }

        foreach (var spriteSwap in
                 button.GetComponentsInChildren<ButtonSpriteSwap>(true))
        {
            spriteSwap.enabled = false;
        }

        foreach (var animation in
                 button.GetComponentsInChildren<Animation>(true))
        {
            animation.Stop();
            animation.enabled = false;
        }

        foreach (var strokable in
                 button.GetComponentsInChildren<Strokable>(true))
        {
            strokable.SetType(Strokable.StrokeType.None);
            strokable.enabled = false;
        }

        foreach (var graphic in button.GetComponentsInChildren<Graphic>(true))
        {
            if (graphic == customBackground ||
                graphic == customLogo ||
                graphic == customLabel)
            {
                continue;
            }

            graphic.raycastTarget = false;
            graphic.enabled = false;
        }

        customBackground.enabled = true;
        customBackground.color = Color.white;
        customBackground.raycastTarget = true;
        button.targetGraphic = customBackground;

        var backgroundTransition =
            button.gameObject.GetComponent<PlatformButtonBackgroundTransition>() ??
            button.gameObject.AddComponent<PlatformButtonBackgroundTransition>();
        backgroundTransition.Initialize(button, customBackground);
    }

    [UIAction("blPressed")]
    private void OnBeatLeaderPressed()
    {
        if (!IsBeatLeaderInstalled)
        {
            Plugin.Log.Warn("BeatLeader was selected, but the BeatLeader plugin is not installed.");
            return;
        }

        _flowCoordinator.ShowBeatLeaderSelect();
    }

    [UIAction("feedbackPressed")]
    private void OnFeedbackPressed()
    {
        UnityEngine.Application.OpenURL(FeedbackUrl);
    }

    [UIAction("exitPressed")]
    private void OnExitPressed()
    {
        _flowCoordinator.Exit();
    }
}
