using System.Collections.Generic;
using System.Linq;
using BeatSaberMarkupLanguage.Attributes;
using BeatSaberMarkupLanguage.Components.Settings;
using BeatLocator.Settings;
using UnityEngine;

namespace BeatLocator.Menu;

internal class SettingsMenu
{
    private const float MinimumSongDurationSeconds = 30f;
    private const float MaximumSongDurationSeconds = 600f;
    private const float SongDurationStepSeconds = 30f;

    private readonly PluginConfig pluginConfig;
    private readonly SettingsApplicator settingsApplicator;

    [UIValue("recommendations-duration-enabled")]
    private bool recommendationsDurationEnabled;

    [UIValue("minimum-song-duration-options")]
    private readonly List<object> minimumSongDurationOptions = CreateSongDurationOptions(
        MinimumSongDurationSeconds);

    [UIValue("minimum-song-duration-choice")]
    private string minimumSongDurationChoice = string.Empty;

    [UIValue("maximum-song-duration-options")]
    private List<object> maximumSongDurationOptions = new List<object>();

    [UIValue("maximum-song-duration-choice")]
    private string maximumSongDurationChoice = string.Empty;

    [UIComponent("maximum-song-duration-dropdown")]
    private readonly DropDownListSetting maximumSongDurationDropdown = null!;

    private float minimumSongDurationSeconds;
    private float maximumSongDurationSeconds;
    
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

        recommendationsDurationEnabled = pluginConfig.RecommendationsDurationEnabled;
        minimumSongDurationSeconds = NormalizeSongDuration(
            pluginConfig.MinimumRecommendedSongDurationSeconds,
            MinimumSongDurationSeconds,
            MaximumSongDurationSeconds);
        maximumSongDurationSeconds = NormalizeSongDuration(
            pluginConfig.MaximumRecommendedSongDurationSeconds,
            minimumSongDurationSeconds,
            MaximumSongDurationSeconds);
        minimumSongDurationChoice = FormatSongDuration(minimumSongDurationSeconds);
        maximumSongDurationChoice = FormatSongDuration(maximumSongDurationSeconds);
        maximumSongDurationOptions = CreateSongDurationOptions(minimumSongDurationSeconds);
        speedAnimationValue = pluginConfig.SpeedAnimationValue;
        endMeChoice = pluginConfig.isEndMePossible ? endMeOptions[1].ToString() : endMeOptions[0].ToString();
    }

    [UIAction("#post-parse")]
    private void PostParse()
    {
        UpdateMaximumSongDurationOptions();
    }

    [UIAction("minimum-song-duration-changed")]
    private void OnMinimumSongDurationChanged(string value)
    {
        minimumSongDurationSeconds = ParseSongDuration(value);
        minimumSongDurationChoice = value;

        if (maximumSongDurationSeconds < minimumSongDurationSeconds)
        {
            maximumSongDurationSeconds = minimumSongDurationSeconds;
            maximumSongDurationChoice = FormatSongDuration(maximumSongDurationSeconds);
        }

        UpdateMaximumSongDurationOptions();
    }

    [UIAction("maximum-song-duration-changed")]
    private void OnMaximumSongDurationChanged(string value)
    {
        maximumSongDurationSeconds = ParseSongDuration(value);
        maximumSongDurationChoice = value;
    }

    private void UpdateMaximumSongDurationOptions()
    {
        maximumSongDurationOptions = CreateSongDurationOptions(minimumSongDurationSeconds);
        maximumSongDurationDropdown.Values = maximumSongDurationOptions;
        maximumSongDurationDropdown.UpdateChoices();
        maximumSongDurationDropdown.Value = maximumSongDurationChoice;
    }

    private static float NormalizeSongDuration(float value, float minimum, float maximum)
    {
        var clampedValue = Mathf.Clamp(value, minimum, maximum);
        var stepCount = Mathf.Round((clampedValue - minimum) / SongDurationStepSeconds);
        return Mathf.Clamp(
            minimum + (stepCount * SongDurationStepSeconds),
            minimum,
            maximum);
    }

    private static List<object> CreateSongDurationOptions(float minimumSeconds)
    {
        var options = new List<object>();
        for (var seconds = minimumSeconds;
             seconds <= MaximumSongDurationSeconds;
             seconds += SongDurationStepSeconds)
        {
            options.Add(FormatSongDuration(seconds));
        }

        return options;
    }

    private static string FormatSongDuration(float seconds)
    {
        var totalSeconds = Mathf.RoundToInt(seconds);
        return $"{totalSeconds / 60}:{totalSeconds % 60:00}";
    }

    private static float ParseSongDuration(string value)
    {
        var parts = value.Split(':');
        return (int.Parse(parts[0]) * 60) + int.Parse(parts[1]);
    }

    [UIAction("#apply")]
    private void Apply()
    {
        // Keep all config writes in one place. Generic BSML settings update the
        // staged UIValue fields before invoking this action.
        pluginConfig.RecommendationsDurationEnabled = recommendationsDurationEnabled;
        pluginConfig.MinimumRecommendedSongDurationSeconds = minimumSongDurationSeconds;
        pluginConfig.MaximumRecommendedSongDurationSeconds = maximumSongDurationSeconds;
        pluginConfig.SpeedAnimationValue = speedAnimationValue;
        pluginConfig.isEndMePossible = endMeChoice == endMeOptions[1].ToString(); // == "Barely Possible"

        settingsApplicator.ApplyAll();
    }
}
