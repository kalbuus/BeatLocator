using System.Threading;
using System.Threading.Tasks;
using BeatLocator.WebUtils;
using UnityEngine;

namespace BeatLocator.EvaluationManagers;

/// <summary>
/// BeatLeader-specific recommendation pipeline. BeatLeader API filters and the
/// pass/tech balance formula live here so they can evolve independently.
/// </summary>
// TODO song duration modifier
internal static class BLEvaluationManager
{
    internal static Task<MapSearchResult> FindMapsAsync(
        bool played,
        float starBuffer,
        bool onlyTwoSaber,
        int mapBalance,
        int mapDifficulty,
        int count,
        PluginConfig config,
        CancellationToken cancellationToken = default)
    {
        return RecommendationEngine.FindMapsAsync(
            RankingProvider.BeatLeader,
            mapDifficulty,
            starBuffer,
            "difficulty, balance, duration, or played-map settings",
            config,
            loadScoresAsync: token => BLWebUtil.FetchCurrentUserScoresAsync(
                RecommendationEngine.ProfilePlayNumber,
                token),
            loadMapsAsync: (expectedStars, currentStarBuffer, token) =>
                BLWebUtil.FindMapAsync(
                    expectedStars,
                    played,
                    currentStarBuffer,
                    onlyTwoSaber,
                    mapBalance,
                    mapDifficulty,
                    count,
                    config,
                    token),
            scoreDifficulty: (map, difficulty, expectedStars, activeConfig) =>
                CalculateDifficultyScore(
                    map,
                    difficulty,
                    mapBalance,
                    expectedStars,
                    activeConfig),
            filterCandidateAsync: null,
            cancellationToken);
    }

    private static float CalculateDifficultyScore(
        RecommendationMap map,
        RecommendationDifficulty difficulty,
        int mapBalance,
        float expectedStars,
        PluginConfig config)
    {
        var difficultyPoints = RecommendationEngine.CalculateDifficultyPoints(
            difficulty,
            expectedStars);
        if (difficulty.PassRating == null || difficulty.TechRating == null)
        {
            return 0f;
        }

        var style =
            (difficulty.TechRating.Value - difficulty.PassRating.Value) /
            (difficulty.TechRating.Value + difficulty.PassRating.Value);
        var stylePoints = CalculateStylePoints(style, mapBalance);
        if (stylePoints == 0f) return 0f;

        if (!config.RecommendationsDurationEnabled)
        {
            return 0.6f * stylePoints + 0.4f * difficultyPoints;
        }

        var durationPoints = RecommendationEngine.CalculateDurationPoints(map, config);
        return 0.5f * stylePoints +
               0.4f * difficultyPoints +
               0.1f * durationPoints;
    }

    private static float CalculateStylePoints(float style, int mapBalance)
    {
        var stylePoints = mapBalance switch
        {
            0 => style switch
            {
                >= 0.5f => 1f,
                > 0f => style / 0.5f,
                _ => 0f
            },
            1 => style switch
            {
                >= 0.3f => 1f,
                >= 0f => 0.5f + style / 0.3f * 0.5f,
                > -0.1f => (1f + style / 0.1f) * 0.25f,
                _ => 0f
            },
            2 => Mathf.Clamp01(1f - Mathf.Abs(style) / 0.15f),
            3 => style switch
            {
                <= -0.3f => 1f,
                <= 0f => 0.5f + -style / 0.3f * 0.5f,
                < 0.1f => (1f - style / 0.1f) * 0.25f,
                _ => 0f
            },
            4 => style switch
            {
                <= -0.5f => 1f,
                < 0f => -style / 0.5f,
                _ => 0f
            },
            _ => 0f
        };

        return Mathf.Clamp01(stylePoints);
    }
}
