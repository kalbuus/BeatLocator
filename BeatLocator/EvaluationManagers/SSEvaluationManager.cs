using System;
using System.Threading;
using System.Threading.Tasks;
using BeatLocator.Settings;
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
        ScoreSaberPlayedFilter playedFilter,
        float starBuffer,
        bool onlyTwoSaber,
        int mapDifficulty,
        SongDurationFilter durationFilter,
        int count,
        PluginConfig config,
        CancellationToken cancellationToken = default)
    {
        var searchContext = SSWebUtil.CreateSearchContext(playedFilter);
        return RecommendationEngine.FindMapsAsync(
            RankingProvider.ScoreSaber,
            mapDifficulty,
            starBuffer,
            "difficulty, duration, or played-map settings",
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
                    durationFilter,
                    count,
                    searchContext,
                    token),
            scoreDifficulty: CalculateDifficultyScore,
            filterCandidateAsync: playedFilter == ScoreSaberPlayedFilter.New
                ? FilterNewCandidateAsync
                : (RecommendationEngine.CandidateFilter?)null,
            cancellationToken);

        async Task<RecommendationEngine.CandidateFilterResult> FilterNewCandidateAsync(
            EvaluatedDifficulty candidate,
            CancellationToken token)
        {
            if (searchContext.NewScoreChecks >= SSWebUtil.MaximumNewScoreChecks)
            {
                return RecommendationEngine.CandidateFilterResult.Failure(
                    $"No unplayed map was found among {SSWebUtil.MaximumNewScoreChecks} " +
                    "matching ScoreSaber difficulties. Try another difficulty or wider filters.");
            }

            try
            {
                var played = await SSWebUtil.HasCurrentUserClearedAsync(
                    candidate,
                    searchContext,
                    token);
                return played
                    ? RecommendationEngine.CandidateFilterResult.Reject()
                    : RecommendationEngine.CandidateFilterResult.Accept();
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (System.Exception exception)
            {
                Plugin.Log.Error(
                    $"Could not check whether the ScoreSaber map was played: {exception}");
                return RecommendationEngine.CandidateFilterResult.Failure(
                    "ScoreSaber could not verify whether a selected map was already played. " +
                    "Please wait a moment and try again.");
            }
        }
    }

    private static float CalculateDifficultyScore(
        RecommendationMap map,
        RecommendationDifficulty difficulty,
        float expectedStars,
        PluginConfig _)
    {
        var difficultyPoints = RecommendationEngine.CalculateDifficultyPoints(
            difficulty,
            expectedStars);
        return difficultyPoints;
    }
}
