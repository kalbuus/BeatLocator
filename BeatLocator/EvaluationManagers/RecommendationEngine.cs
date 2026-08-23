using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace BeatLocator.EvaluationManagers;

/// <summary>
/// Provider-neutral recommendation mechanics. Provider managers own their API
/// calls, filters, and scoring formulas; this class owns only the shared player
/// skill model, adaptive star search, session history, and weighted selection.
/// </summary>
internal static class RecommendationEngine
{
    internal const int ProfilePlayNumber = 100;

    private const float MinimumTargetStars = 1f;
    private const float MaximumTargetStars = 15f;
    private const float StarBufferStep = 0.5f;
    private static readonly System.Random Random = new System.Random();
    private static readonly List<RecommendationMap> SessionSongHistoryEntries =
        new List<RecommendationMap>();
    private static readonly HashSet<string> SessionSongHistoryKeys =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<RankingProvider, List<PlayerScoreEntry>> ProfileScores =
        new Dictionary<RankingProvider, List<PlayerScoreEntry>>();
    private static readonly Dictionary<RankingProvider, DifficultyRange> PlayerDifficultyRanges =
        new Dictionary<RankingProvider, DifficultyRange>();

    internal delegate Task<List<PlayerScoreEntry>?> ScoreLoader(
        CancellationToken cancellationToken);

    internal delegate Task<List<RecommendationMap>?> MapLoader(
        float expectedStars,
        float starBuffer,
        CancellationToken cancellationToken);

    internal delegate float DifficultyScorer(
        RecommendationMap map,
        RecommendationDifficulty difficulty,
        float expectedStars,
        PluginConfig config);

    internal readonly struct PlaySample
    {
        internal PlaySample(float stars, float accuracy, float weight)
        {
            Stars = stars;
            Accuracy = accuracy;
            Weight = weight;
        }

        internal float Stars { get; }
        internal float Accuracy { get; }
        internal float Weight { get; }
    }

    internal readonly struct DifficultyRange
    {
        internal DifficultyRange(
            float minimum,
            float low,
            float medium,
            float high,
            float maximum)
        {
            Minimum = minimum;
            Low = low;
            Medium = medium;
            High = high;
            Maximum = maximum;
        }

        internal float Minimum { get; }
        internal float Low { get; }
        internal float Medium { get; }
        internal float High { get; }
        internal float Maximum { get; }

        internal float GetStars(int difficultyPreset)
        {
            return difficultyPreset switch
            {
                0 => Minimum,
                1 => Low,
                2 => Medium,
                3 => High,
                4 => Maximum,
                _ => throw new ArgumentOutOfRangeException(
                    nameof(difficultyPreset),
                    difficultyPreset,
                    "Difficulty preset must be between 0 and 4.")
            };
        }
    }

    private readonly struct AccuracyModel
    {
        internal AccuracyModel(double intercept, double slope)
        {
            Intercept = intercept;
            Slope = slope;
        }

        internal double Intercept { get; }
        internal double Slope { get; }
    }

    private readonly struct DifficultyProfileResult
    {
        private DifficultyProfileResult(DifficultyRange range, string? failureReason)
        {
            Range = range;
            FailureReason = failureReason;
        }

        internal DifficultyRange Range { get; }
        internal string? FailureReason { get; }
        internal bool IsSuccess => FailureReason == null;

        internal static DifficultyProfileResult Success(DifficultyRange range)
        {
            return new DifficultyProfileResult(range, null);
        }

        internal static DifficultyProfileResult Failure(string reason)
        {
            return new DifficultyProfileResult(default, reason);
        }
    }

    internal static async Task<MapSearchResult> FindMapsAsync(
        RankingProvider provider,
        int mapDifficulty,
        float starBuffer,
        string filterDescription,
        PluginConfig config,
        ScoreLoader loadScoresAsync,
        MapLoader loadMapsAsync,
        DifficultyScorer scoreDifficulty,
        CancellationToken cancellationToken = default)
    {
        var profileResult = await GetDifficultyRangeAsync(
            provider,
            config,
            loadScoresAsync,
            cancellationToken);
        if (!profileResult.IsSuccess)
        {
            return MapSearchResult.Failure(
                profileResult.FailureReason ??
                "Your recommended difficulty could not be calculated.");
        }

        float expectedStars;
        try
        {
            expectedStars = profileResult.Range.GetStars(mapDifficulty);
        }
        catch (ArgumentOutOfRangeException exception)
        {
            Plugin.Log.Error(
                $"Could not select the requested player difficulty preset: {exception}");
            return MapSearchResult.Failure(
                "The selected difficulty preset is invalid. Please choose another difficulty.");
        }

        var maximumUsefulBuffer = Math.Max(
            expectedStars - MinimumTargetStars,
            MaximumTargetStars - expectedStars);
        var currentStarBuffer = Math.Min(
            Math.Max(0f, starBuffer),
            maximumUsefulBuffer);

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var requestedMaps = await loadMapsAsync(
                expectedStars,
                currentStarBuffer,
                cancellationToken);
            if (requestedMaps == null)
            {
                var serviceName = provider.GetDisplayName();
                return MapSearchResult.Failure(
                    $"{serviceName} could not complete the map request. " +
                    "Check your sign-in and internet connection, then try again.");
            }

            var availableMaps = ExcludeSessionHistory(requestedMaps);
            if (requestedMaps.Count > 0 && availableMaps.Count == 0)
            {
                return MapSearchResult.Failure(
                    "All matching maps have already been shown during this Beat Saber session. " +
                    "Restart the game or change the filters.");
            }

            var evaluatedDifficulties = EvaluateMapDifficulties(
                availableMaps,
                expectedStars,
                config,
                scoreDifficulty);
            var selectedDifficulty = SelectRandomDifficulty(evaluatedDifficulties);
            if (selectedDifficulty != null)
            {
                AddToSessionHistory(selectedDifficulty.Map);
                LogSelection(selectedDifficulty);
                return MapSearchResult.Success(selectedDifficulty);
            }

            if (currentStarBuffer >= maximumUsefulBuffer)
            {
                Plugin.Log.Warn(
                    "No difficulty with a positive score was found after searching " +
                    $"the full {MinimumTargetStars:0.#}-{MaximumTargetStars:0.#} star range.");
                return MapSearchResult.Failure(
                    "No ranked maps matched your profile and current filters. " +
                    $"Try changing the {filterDescription}.");
            }

            currentStarBuffer = Math.Min(
                currentStarBuffer + StarBufferStep,
                maximumUsefulBuffer);
        }
    }

    internal static void RecalculatePlayerDifficultyRanges(PluginConfig config)
    {
        foreach (var cachedScores in ProfileScores.ToArray())
        {
            try
            {
                var difficultyRange = CalculateCenteredDifficultyRange(
                    CreatePlaySamples(cachedScores.Value),
                    config.isEndMePossible);
                SetPlayerDifficultyRange(
                    cachedScores.Key,
                    difficultyRange,
                    "Recalculated");
            }
            catch (Exception exception) when (
                exception is ArgumentException ||
                exception is InvalidOperationException)
            {
                Plugin.Log.Error(
                    $"Could not recalculate the {cachedScores.Key.GetDisplayName()} " +
                    $"player difficulty range: {exception.Message}");
            }
        }
    }

    internal static float CalculateDifficultyPoints(
        RecommendationDifficulty difficulty,
        float expectedStars)
    {
        if (difficulty.Stars == null) return 0f;

        var difficultyDifference = Mathf.Abs(
            (difficulty.Stars.Value - expectedStars) /
            (difficulty.Stars.Value + expectedStars));
        var difficultyPoints = difficultyDifference <= 1.5f
            ? 1f - difficultyDifference / 1.5f
            : 0f;
        return Mathf.Clamp01(difficultyPoints);
    }

    internal static float CalculateDurationPoints(
        RecommendationMap map,
        PluginConfig config)
    {
        if (map.Duration >= config.MinimumRecommendedSongDurationSeconds &&
            map.Duration <= config.MaximumRecommendedSongDurationSeconds)
        {
            return 1f;
        }

        var distance =
            map.Duration < config.MinimumRecommendedSongDurationSeconds
                ? config.MinimumRecommendedSongDurationSeconds - map.Duration
                : map.Duration - config.MaximumRecommendedSongDurationSeconds;
        return Mathf.Max(0f, 1f - (distance ?? 15f) / 15f);
    }

    internal static DifficultyRange CalculateCenteredDifficultyRange(
        IEnumerable<PlaySample> source,
        bool isEndMePossible)
    {
        if (source == null) throw new ArgumentNullException(nameof(source));

        var samples = source
            .Where(sample =>
                IsFinite(sample.Stars) &&
                IsFinite(sample.Accuracy) &&
                IsFinite(sample.Weight) &&
                sample.Stars > 0f &&
                sample.Accuracy > 0f &&
                sample.Accuracy < 1f &&
                sample.Weight > 0f)
            .ToArray();
        if (samples.Length < 5)
        {
            throw new ArgumentException(
                "At least five valid plays are required.",
                nameof(source));
        }

        var model = FitAccuracyModel(samples);
        var centerStars = Clamp(
            GetWeightedAverageStars(samples),
            MinimumTargetStars,
            MaximumTargetStars);
        var centerLogit = model.Intercept + model.Slope * centerStars;

        const float innerLogitOffset = 0.35f;
        const float highLogitOffset = 0.25f;
        const float outerLogitOffset = 0.75f;

        var minimum = GetBoundedStarsForLogit(
            model,
            centerLogit + outerLogitOffset,
            centerStars,
            maximumDistance: 3f);
        var low = GetBoundedStarsForLogit(
            model,
            centerLogit + innerLogitOffset,
            centerStars,
            maximumDistance: 1.75f);
        var high = GetBoundedStarsForLogit(
            model,
            centerLogit - highLogitOffset,
            centerStars,
            maximumDistance: 1.75f);
        var maximumOffset = isEndMePossible
            ? innerLogitOffset
            : outerLogitOffset;
        var maximum = GetBoundedStarsForLogit(
            model,
            centerLogit - maximumOffset,
            centerStars,
            maximumDistance: 3f);

        return new DifficultyRange(minimum, low, centerStars, high, maximum);
    }

    private static async Task<DifficultyProfileResult> GetDifficultyRangeAsync(
        RankingProvider provider,
        PluginConfig config,
        ScoreLoader loadScoresAsync,
        CancellationToken cancellationToken)
    {
        if (PlayerDifficultyRanges.TryGetValue(provider, out var cachedRange))
        {
            return DifficultyProfileResult.Success(cachedRange);
        }

        var scores = await loadScoresAsync(cancellationToken);
        if (scores == null)
        {
            var serviceName = provider.GetDisplayName();
            return DifficultyProfileResult.Failure(
                $"Could not load your {serviceName} score history. " +
                $"Make sure {serviceName} is signed in and the service is reachable.");
        }

        DifficultyRange difficultyRange;
        try
        {
            difficultyRange = CalculateCenteredDifficultyRange(
                CreatePlaySamples(scores),
                config.isEndMePossible);
        }
        catch (Exception exception) when (
            exception is ArgumentException ||
            exception is InvalidOperationException)
        {
            Plugin.Log.Error(
                $"Could not calculate the player difficulty range: {exception.Message}");
            return DifficultyProfileResult.Failure(
                $"Could not calculate your recommended difficulty: {exception.Message}");
        }

        ProfileScores[provider] = scores;
        SetPlayerDifficultyRange(provider, difficultyRange, "Calculated");
        return DifficultyProfileResult.Success(difficultyRange);
    }

    private static void SetPlayerDifficultyRange(
        RankingProvider provider,
        DifficultyRange difficultyRange,
        string operation)
    {
        PlayerDifficultyRanges[provider] = difficultyRange;
        Plugin.Log.Info(
            $"{operation} {provider.GetDisplayName()} difficulty stars: " +
            $"{difficultyRange.Minimum:0.00}, " +
            $"{difficultyRange.Low:0.00}, " +
            $"{difficultyRange.Medium:0.00}, " +
            $"{difficultyRange.High:0.00}, " +
            $"{difficultyRange.Maximum:0.00}.");
    }

    private static IEnumerable<PlaySample> CreatePlaySamples(
        IReadOnlyList<PlayerScoreEntry> attempts)
    {
        foreach (var attempt in attempts)
        {
            var stars = attempt.Leaderboard?.Difficulty?.Stars;
            if (!stars.HasValue) continue;

            const float halfLifeDays = 30f;
            var playTime = DateTimeOffset.FromUnixTimeSeconds(attempt.Timepost);
            var daysAgo = (DateTimeOffset.UtcNow - playTime).TotalDays;
            var dateWeight = Mathf.Pow(0.5f, (float)(daysAgo / halfLifeDays));
            var accuracyWeight = attempt.Accuracy switch
            {
                < 0.70f => 0.1f,
                < 0.80f => 0.5f,
                <= 0.97f => 1f,
                <= 0.99f => 0.6f,
                _ => 0.25f
            };

            yield return new PlaySample(
                stars.Value,
                attempt.Accuracy,
                dateWeight * 0.3f + accuracyWeight * 0.7f);
        }
    }

    private static List<EvaluatedDifficulty> EvaluateMapDifficulties(
        IEnumerable<RecommendationMap> maps,
        float expectedStars,
        PluginConfig config,
        DifficultyScorer scoreDifficulty)
    {
        var evaluatedDifficulties = new List<EvaluatedDifficulty>();
        foreach (var map in maps)
        {
            if (map.Difficulties == null) continue;

            foreach (var difficulty in map.Difficulties)
            {
                evaluatedDifficulties.Add(new EvaluatedDifficulty(
                    map,
                    difficulty,
                    scoreDifficulty(map, difficulty, expectedStars, config)));
            }
        }

        return evaluatedDifficulties;
    }

    private static List<RecommendationMap> ExcludeSessionHistory(
        IEnumerable<RecommendationMap> maps)
    {
        return maps.Where(map => !IsInSessionHistory(map)).ToList();
    }

    private static bool IsInSessionHistory(RecommendationMap map)
    {
        return GetSessionHistoryKeys(map).Any(key => SessionSongHistoryKeys.Contains(key));
    }

    private static void AddToSessionHistory(RecommendationMap map)
    {
        if (IsInSessionHistory(map)) return;

        SessionSongHistoryEntries.Add(map);
        foreach (var key in GetSessionHistoryKeys(map))
        {
            SessionSongHistoryKeys.Add(key);
        }
    }

    private static IEnumerable<string> GetSessionHistoryKeys(RecommendationMap map)
    {
        var hasStableKey = false;
        var hash = map.Hash;
        if (!string.IsNullOrWhiteSpace(hash))
        {
            hasStableKey = true;
            yield return "hash:" + hash!.Trim();
        }

        var id = map.Id;
        if (!string.IsNullOrWhiteSpace(id))
        {
            hasStableKey = true;
            yield return "id:" + id!.Trim();
        }

        var downloadUrl = map.DownloadUrl;
        if (!string.IsNullOrWhiteSpace(downloadUrl))
        {
            hasStableKey = true;
            yield return "download:" + downloadUrl!.Trim();
        }

        var name = map.Name;
        if (!hasStableKey && !string.IsNullOrWhiteSpace(name))
        {
            yield return "name:" + name!.Trim() +
                         "|mapper:" + (map.Mapper ?? string.Empty).Trim();
        }
    }

    private static EvaluatedDifficulty? SelectRandomDifficulty(
        IReadOnlyCollection<EvaluatedDifficulty> difficulties)
    {
        var totalWeight = difficulties
            .Where(difficulty => IsValidWeight(difficulty.Score))
            .Sum(difficulty => (double)difficulty.Score);
        if (totalWeight <= 0d) return null;

        double selectionPoint;
        lock (Random)
        {
            selectionPoint = Random.NextDouble() * totalWeight;
        }

        EvaluatedDifficulty? lastEligibleDifficulty = null;
        foreach (var difficulty in difficulties)
        {
            if (!IsValidWeight(difficulty.Score)) continue;

            lastEligibleDifficulty = difficulty;
            selectionPoint -= difficulty.Score;
            if (selectionPoint < 0d) return difficulty;
        }

        return lastEligibleDifficulty;
    }

    private static void LogSelection(EvaluatedDifficulty selectedDifficulty)
    {
        var map = selectedDifficulty.Map;
        var difficulty = selectedDifficulty.Difficulty;
        Plugin.Log.Info(
            $"Selected difficulty: {map.Name ?? "<unnamed map>"} - " +
            $"{difficulty.DifficultyName ?? "<unnamed difficulty>"} " +
            $"({difficulty.ModeName ?? "<unknown mode>"}), " +
            $"{difficulty.Stars:0.00} stars, score: {selectedDifficulty.Score:0.000}.");
    }

    private static bool IsValidWeight(float score)
    {
        return score > 0f && !float.IsNaN(score) && !float.IsInfinity(score);
    }

    private static float GetWeightedAverageStars(IEnumerable<PlaySample> samples)
    {
        double weightedSum = 0d;
        double totalWeight = 0d;
        foreach (var sample in samples)
        {
            if (sample.Weight <= 0f) continue;
            weightedSum += sample.Stars * sample.Weight;
            totalWeight += sample.Weight;
        }

        if (totalWeight <= 0d)
        {
            throw new InvalidOperationException("Total sample weight must be positive.");
        }

        return (float)(weightedSum / totalWeight);
    }

    private static float GetBoundedStarsForLogit(
        AccuracyModel model,
        double targetLogit,
        float centerStars,
        float maximumDistance)
    {
        if (Math.Abs(model.Slope) < 0.000001d)
        {
            throw new InvalidOperationException("Accuracy curve slope is too small.");
        }

        var stars = (float)((targetLogit - model.Intercept) / model.Slope);
        return Clamp(
            stars,
            Math.Max(MinimumTargetStars, centerStars - maximumDistance),
            Math.Min(MaximumTargetStars, centerStars + maximumDistance));
    }

    private static AccuracyModel FitAccuracyModel(
        IReadOnlyCollection<PlaySample> samples)
    {
        double totalWeight = 0d;
        double weightedStars = 0d;
        double weightedLogitAccuracy = 0d;
        foreach (var sample in samples)
        {
            var accuracy = Clamp(sample.Accuracy, 0.01, 0.995);
            var logitAccuracy = Math.Log(accuracy / (1d - accuracy));
            totalWeight += sample.Weight;
            weightedStars += sample.Stars * sample.Weight;
            weightedLogitAccuracy += logitAccuracy * sample.Weight;
        }

        if (totalWeight <= 0d)
        {
            throw new ArgumentException("Total weight must be positive.");
        }

        var meanStars = weightedStars / totalWeight;
        var meanLogitAccuracy = weightedLogitAccuracy / totalWeight;
        double covariance = 0d;
        double variance = 0d;
        foreach (var sample in samples)
        {
            var accuracy = Clamp(sample.Accuracy, 0.01, 0.995);
            var logitAccuracy = Math.Log(accuracy / (1d - accuracy));
            var starsDifference = sample.Stars - meanStars;
            var accuracyDifference = logitAccuracy - meanLogitAccuracy;
            covariance += sample.Weight * starsDifference * accuracyDifference;
            variance += sample.Weight * starsDifference * starsDifference;
        }

        if (variance < 0.000001d)
        {
            throw new InvalidOperationException(
                "Played maps have too little star variation.");
        }

        var slope = covariance / variance;
        var intercept = meanLogitAccuracy - slope * meanStars;
        if (slope >= -0.01d)
        {
            throw new InvalidOperationException(
                "Could not determine a meaningful difficulty curve.");
        }

        return new AccuracyModel(intercept, slope);
    }

    private static bool IsFinite(float value)
    {
        return !float.IsNaN(value) && !float.IsInfinity(value);
    }

    private static float Clamp(float value, float minimum, float maximum)
    {
        return Math.Min(Math.Max(value, minimum), maximum);
    }

    private static double Clamp(double value, double minimum, double maximum)
    {
        return Math.Min(Math.Max(value, minimum), maximum);
    }
}
