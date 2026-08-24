using System.Collections.Generic;
using System.Linq;
using BeatSaberMarkupLanguage.Attributes;
using BeatLocator.Settings;

namespace BeatLocator.Menu;

internal class SettingsMenu
{
    private readonly PluginConfig pluginConfig;
    private readonly SettingsApplicator settingsApplicator;
    
    [UIValue("speed-animation-value")]
    private float speedAnimationValue = 1f;
    
    [UIValue("end-me-options")]
    private List<object> endMeOptions = new object[] { "Actually Impossible", "Barely Possible" }.ToList();

    [UIValue("end-me-choice")]
    private string endMeChoice = "Barely Possible";

    public SettingsMenu(PluginConfig pluginConfig, SettingsApplicator settingsApplicator)
    {
        this.pluginConfig = pluginConfig;
        this.settingsApplicator = settingsApplicator;

        speedAnimationValue = pluginConfig.SpeedAnimationValue;
        endMeChoice = pluginConfig.isEndMePossible ? endMeOptions[1].ToString() : endMeOptions[0].ToString();
    }

    [UIAction("#apply")]
    private void Apply()
    {
        // Keep all config writes in one place. Generic BSML settings update the
        // staged UIValue fields before invoking this action.
        pluginConfig.SpeedAnimationValue = speedAnimationValue;
        pluginConfig.isEndMePossible = endMeChoice == endMeOptions[1].ToString(); // == "Barely Possible"

        settingsApplicator.ApplyAll();
    }
}
