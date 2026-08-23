using System.Threading;
using System.Threading.Tasks;
using BeatLocator.WebUtils;

namespace BeatLocator.EvaluationManagers;

/// <summary>
/// ScoreSaber-specific recommendation pipeline. It has no song balance as seen in BeatLeader because of how closed their API is.
/// </summary>
// TODO Make a new algorithm for player-specific map finding (like in bl)
internal static class SSEvaluationManager
{
    internal static Task<MapSearchResult> FindMapsAsync(
        IPlatformUserModel platformUserModel,
        float starBuffer,
        bool onlyTwoSaber,
        int mapDifficulty,
        int count,
        PluginConfig config,
        CancellationToken cancellationToken = default)
    {
        return RecommendationEngine.FindMapsAsync(
            RankingProvider.ScoreSaber,
            mapDifficulty,
            starBuffer,
            "difficulty or duration settings",
            config,
            loadScoresAsync: token => SSWebUtil.FetchCurrentUserScoresAsync(
                platformUserModel,
                RecommendationEngine.ProfilePlayNumber,
                token),
            loadMapsAsync: (expectedStars, currentStarBuffer, token) =>
                SSWebUtil.FindMapAsync(
                    expectedStars,
                    currentStarBuffer,
                    onlyTwoSaber,
                    mapDifficulty,
                    count,
                    token),
            scoreDifficulty: CalculateDifficultyScore,
            cancellationToken);
    }

    private static float CalculateDifficultyScore(
        RecommendationMap map,
        RecommendationDifficulty difficulty,
        float expectedStars,
        PluginConfig config)
    {
        var difficultyPoints = RecommendationEngine.CalculateDifficultyPoints(
            difficulty,
            expectedStars);
        if (!config.RecommendationsDurationEnabled) return difficultyPoints;

        var durationPoints = RecommendationEngine.CalculateDurationPoints(map, config);
        return 0.9f * difficultyPoints + 0.1f * durationPoints;
    }
}
