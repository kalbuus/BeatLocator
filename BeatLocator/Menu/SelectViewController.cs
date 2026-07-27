using BeatSaberMarkupLanguage.Attributes;
using BeatSaberMarkupLanguage.ViewControllers;
using IPA.Loader;
using UnityEngine.UI;
using Zenject;

namespace BeatLocator.Menu;

public class SelectViewController : BSMLAutomaticViewController
{
    private const string FeedbackUrl =
        "https://docs.google.com/forms/d/e/1FAIpQLScuMAm1XBrDSmW3VGs5oYQw_WkMC3xXz_o9N_OV9fYRN0LJzQ/viewform?usp=sharing&ouid=110581073513055024563";

    [UIComponent("beatleader-button")]
    private Button _beatLeaderButton = null!;

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
