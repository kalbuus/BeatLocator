using BeatLocator.EvaluationManagers;

namespace BeatLocator.PostLevel;

/// <summary>Native result validation and raw accuracy, shared by immediate recommendation learning.</summary>
internal static class LocalSkillObservation
{
    internal static bool TryRead(StandardLevelScenesTransitionSetupDataSO transition,
        LevelCompletionResults results, RankingProvider provider, RecommendationDifficulty difficulty,
        out float accuracy, out string speed, out bool failed)
    {
        accuracy = 0; speed = string.Empty;
        failed = results.levelEndStateType == LevelCompletionResults.LevelEndStateType.Failed;
        if (failed) return false; // A failure cannot distinguish lost form from a pattern mismatch.
        if (transition.practiceSettings != null || results.invalidated ||
            results.levelEndAction != LevelCompletionResults.LevelEndAction.None ||
            (!failed && results.levelEndStateType != LevelCompletionResults.LevelEndStateType.Cleared)) return false;
        var mods = results.gameplayModifiers;
        if (mods == null || mods.noFailOn0Energy || mods.zenMode || mods.noArrows || mods.noBombs ||
            mods.enabledObstacleType != GameplayModifiers.EnabledObstacleType.All || mods.proMode || mods.smallCubes)
            return false;
        if (failed && (mods.instaFail || mods.failOnSaberClash || mods.energyType == GameplayModifiers.EnergyType.Battery))
            return false;
        speed = mods.songSpeed switch
        {
            GameplayModifiers.SongSpeed.Faster => "FS", GameplayModifiers.SongSpeed.SuperFast => "SF",
            GameplayModifiers.SongSpeed.Slower => "SS", _ => string.Empty
        };
        if (provider == RankingProvider.ScoreSaber && speed.Length > 0) return false;
        var maximum = transition.transformedBeatmapData == null ? difficulty.MaxScore :
            ScoreModel.ComputeMaxMultipliedScoreForBeatmap(transition.transformedBeatmapData);
        if (maximum <= 0 && !failed) return false;
        accuracy = failed ? 0f : results.multipliedScore / (float)maximum;
        if (!failed && (accuracy <= 0 || accuracy > 1 || float.IsNaN(accuracy))) return false;
        return true;
    }
}
