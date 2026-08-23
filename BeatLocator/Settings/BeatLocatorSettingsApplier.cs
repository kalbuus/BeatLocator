using BeatLocator.EvaluationManagers;

namespace BeatLocator.Settings;

internal class BeatLocatorSettingsApplier : ISettingsApplier
{
    public void Apply(PluginConfig config)
    {
        RecommendationEngine.RecalculatePlayerDifficultyRanges(config);
    }
}
