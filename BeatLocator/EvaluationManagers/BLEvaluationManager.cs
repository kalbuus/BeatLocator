using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BeatLocator.WebUtils;
using UnityEngine;

namespace BeatLocator.EvaluationManagers;

public static class BLEvaluationManager
{
    private const int ProfilePlayNumber = 100;
    private const float MinimumTargetStars = 1f;
    private const float MaximumTargetStars = 15f;
    private const float StarBufferStep = 0.5f;
    private static readonly System.Random Random = new System.Random();
    private static readonly List<BLWebUtil.MapEntry> SessionSongHistoryEntries =
        new List<BLWebUtil.MapEntry>();
    private static readonly HashSet<string> SessionSongHistoryKeys =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    private static List<BLWebUtil.ScoreEntry>? profileScores;

    public static DifficultyRange? PlayerDifficultyRange;
    public static List<BLWebUtil.MapEntry>? Maps;
    public static List<EvaluatedDifficulty>? EvaluatedDifficulties;
    public static EvaluatedDifficulty? SelectedDifficulty;
    public static IReadOnlyList<BLWebUtil.MapEntry> SessionSongHistory =>
        SessionSongHistoryEntries.AsReadOnly();

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
    
    public readonly struct PlaySample
    {
        public PlaySample(float stars, float accuracy, float weight)
        {
            Stars = stars;
            Accuracy = accuracy;
            Weight = weight;
        }

        public float Stars { get; }

        public float Accuracy { get; }

        public float Weight { get; }
    }

    public readonly struct DifficultyRange
    {
        public DifficultyRange(
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

        public float Minimum { get; }

        public float Low { get; }

        public float Medium { get; }

        public float High { get; }

        public float Maximum { get; }

        public float GetStars(int difficultyPreset)
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
        public AccuracyModel(
            double intercept,
            double slope)
        {
            Intercept = intercept;
            Slope = slope;
        }

        public double Intercept { get; }

        public double Slope { get; }
    }

    internal static async Task EvaluateScoresAsync(
        PluginConfig config,
        CancellationToken cancellationToken = default)
    {
        if (profileScores != null && PlayerDifficultyRange.HasValue) return;
        
        var tmpScores = await BLWebUtil.FetchCurrentUserScoresAsync(
            ProfilePlayNumber,
            cancellationToken);

        if (tmpScores == null)
        {
            return;
        }

        DifficultyRange difficultyRange;
        try
        {
            difficultyRange = CalculateCenteredDifficultyRange(CreatePlaySamples(tmpScores), config.isEndMePossible);
        }
        catch (Exception exception) when (
            exception is ArgumentException ||
            exception is InvalidOperationException)
        {
            Plugin.Log.Error($"Could not calculate the player difficulty range: {exception.Message}");
            return;
        }

        profileScores = tmpScores;
        SetPlayerDifficultyRange(difficultyRange, "Calculated");
    }

    /// <summary>
    /// Recalculates the cached player range after an applied setting changes
    /// the evaluation model, without downloading the score history again.
    /// </summary>
    internal static void RecalculatePlayerDifficultyRange(PluginConfig config)
    {
        if (profileScores == null) return;

        try
        {
            var difficultyRange = CalculateCenteredDifficultyRange(
                CreatePlaySamples(profileScores),
                config.isEndMePossible);
            SetPlayerDifficultyRange(difficultyRange, "Recalculated");
        }
        catch (Exception exception) when (
            exception is ArgumentException ||
            exception is InvalidOperationException)
        {
            Plugin.Log.Error(
                $"Could not recalculate the player difficulty range: {exception.Message}");
        }
    }

    private static void SetPlayerDifficultyRange(
        DifficultyRange difficultyRange,
        string operation)
    {
        PlayerDifficultyRange = difficultyRange;

        Plugin.Log.Info(
            $"{operation} difficulty stars: " +
            $"{difficultyRange.Minimum:0.00}, " +
            $"{difficultyRange.Low:0.00}, " +
            $"{difficultyRange.Medium:0.00}, " +
            $"{difficultyRange.High:0.00}, " +
            $"{difficultyRange.Maximum:0.00}.");
    }

    private static IEnumerable<PlaySample> CreatePlaySamples(
        IReadOnlyList<BLWebUtil.ScoreEntry> attempts)
    {
        if (attempts == null) throw new ArgumentNullException(nameof(attempts));

        foreach (var attempt in attempts)
        {
            var stars = attempt.Leaderboard?.Difficulty?.Stars;
            if (!stars.HasValue) continue;

            const float HalfLifeDays = 30f;

            DateTimeOffset playTime = DateTimeOffset.FromUnixTimeSeconds(attempt.Timepost);
            double daysAgo = (DateTimeOffset.UtcNow - playTime).TotalDays;

            float dateWeight = Mathf.Pow(0.5f, (float)(daysAgo / HalfLifeDays));
            
            float accuracyWeight = attempt.Accuracy switch
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
    
    public static DifficultyRange CalculateCenteredDifficultyRange(
        IEnumerable<PlaySample> source, bool isEndMePossible)
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
        float centerStars = Clamp(
            GetWeightedAverageStars(samples),
            MinimumTargetStars,
            MaximumTargetStars);

        double centerLogit =
            model.Intercept +
            model.Slope * centerStars;

        const float innerLogitOffset = 0.35f;
        const float outerLogitOffset = 0.75f;

        float minimum = GetBoundedStarsForLogit(
            model,
            centerLogit + outerLogitOffset,
            centerStars,
            maximumDistance: 3f);

        float low = GetBoundedStarsForLogit(
            model,
            centerLogit + innerLogitOffset,
            centerStars,
            maximumDistance: 1.75f);

        float high = GetBoundedStarsForLogit(
            model,
            centerLogit - innerLogitOffset,
            centerStars,
            maximumDistance: 1.75f);
        
        
        float maximumOffset = isEndMePossible
            ? innerLogitOffset
            : outerLogitOffset;

        float maximum = GetBoundedStarsForLogit(
            model,
            centerLogit - maximumOffset,
            centerStars,
            maximumDistance: 3f);

        return new DifficultyRange(
            minimum,
            low,
            centerStars,
            high,
            maximum
        );
    }
    
    private static float GetWeightedAverageStars(
        IEnumerable<PlaySample> samples)
    {
        double weightedSum = 0d;
        double totalWeight = 0d;

        foreach (var sample in samples)
        {
            if (sample.Weight <= 0f)
                continue;

            weightedSum += sample.Stars * sample.Weight;
            totalWeight += sample.Weight;
        }

        if (totalWeight <= 0d)
            throw new InvalidOperationException(
                "Total sample weight must be positive.");

        return (float)(weightedSum / totalWeight);
    }

    private static float GetStarsForLogit(
        AccuracyModel model,
        double targetLogit)
    {
        if (Math.Abs(model.Slope) < 0.000001d)
            throw new InvalidOperationException(
                "Accuracy curve slope is too small.");

        return (float)(
            (targetLogit - model.Intercept) /
            model.Slope
        );
    }
    
    private static float GetBoundedStarsForLogit(
        AccuracyModel model,
        double targetLogit,
        float centerStars,
        float maximumDistance)
    {
        float stars = GetStarsForLogit(model, targetLogit);

        return Clamp(
            stars,
            Math.Max(MinimumTargetStars, centerStars - maximumDistance),
            Math.Min(MaximumTargetStars, centerStars + maximumDistance)
        );
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
            var weight = sample.Weight;

            totalWeight += weight;
            weightedStars += sample.Stars * weight;
            weightedLogitAccuracy += logitAccuracy * weight;

        }

        if (totalWeight <= 0d)
            throw new ArgumentException("Total weight must be positive.");

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

            covariance +=
                sample.Weight *
                starsDifference *
                accuracyDifference;

            variance +=
                sample.Weight *
                starsDifference *
                starsDifference;
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

        return new AccuracyModel(
            intercept,
            slope);
    }

    internal static async Task FindMapsAsync(bool played, float starBuffer, bool onlyTwoSaber,
                                           int mapBalance, int mapDifficulty, int count, PluginConfig? config,
                                           CancellationToken cancellationToken = default)
    {
        if (config == null)
        {
            Plugin.Log.Error("Could not find maps because the BeatLocator configuration is unavailable.");
            return;
        }
        
        SelectedDifficulty = null;

        if (!PlayerDifficultyRange.HasValue)
        {
            await EvaluateScoresAsync(config, cancellationToken);
        }

        if (!PlayerDifficultyRange.HasValue)
        {
            return;
        }

        float expectedStars;
        try
        {
            expectedStars = PlayerDifficultyRange.Value.GetStars(mapDifficulty);
        }
        catch (ArgumentOutOfRangeException exception)
        {
            Plugin.Log.Error($"Could not select the requested player difficulty preset: {exception}");
            return;
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
            var tmpMaps = await BLWebUtil.FindMapAsync(
                expectedStars,
                played,
                currentStarBuffer,
                onlyTwoSaber,
                mapBalance,
                mapDifficulty,
                count,
                config,
                cancellationToken);

            if (tmpMaps == null)
            {
                return;
            }

            Maps = ExcludeSessionHistory(tmpMaps);
            EvaluatedDifficulties = EvaluateMapDifficulties(
                Maps,
                mapBalance,
                expectedStars,
                config);

            SelectedDifficulty = SelectRandomDifficulty(EvaluatedDifficulties);
            if (SelectedDifficulty != null) break;

            if (currentStarBuffer >= maximumUsefulBuffer)
            {
                Plugin.Log.Warn(
                    "No difficulty with a positive score was found after searching " +
                    $"the full {MinimumTargetStars:0.#}-{MaximumTargetStars:0.#} star range.");
                return;
            }

            var nextStarBuffer = Math.Min(
                currentStarBuffer + StarBufferStep,
                maximumUsefulBuffer);
            currentStarBuffer = nextStarBuffer;
        }

        var selectedMap = SelectedDifficulty.Map;
        var selectedDifficulty = SelectedDifficulty.Difficulty;
        AddToSessionHistory(selectedMap);
        Plugin.Log.Info(
            $"Selected difficulty: {selectedMap.Name ?? "<unnamed map>"} - " +
            $"{selectedDifficulty.DifficultyName ?? "<unnamed difficulty>"} " +
            $"({selectedDifficulty.ModeName ?? "<unknown mode>"}), " +
            $"{selectedDifficulty.Stars:0.00} stars, score: {SelectedDifficulty.Score:0.000}.");
    }

    private static List<BLWebUtil.MapEntry> ExcludeSessionHistory(
        IEnumerable<BLWebUtil.MapEntry> maps)
    {
        var availableMaps = new List<BLWebUtil.MapEntry>();

        foreach (var map in maps)
        {
            if (!IsInSessionHistory(map))
            {
                availableMaps.Add(map);
            }
        }

        return availableMaps;
    }

    private static bool IsInSessionHistory(BLWebUtil.MapEntry map)
    {
        foreach (var key in GetSessionHistoryKeys(map))
        {
            if (SessionSongHistoryKeys.Contains(key)) return true;
        }

        return false;
    }

    private static void AddToSessionHistory(BLWebUtil.MapEntry map)
    {
        if (IsInSessionHistory(map)) return;

        SessionSongHistoryEntries.Add(map);
        foreach (var key in GetSessionHistoryKeys(map))
        {
            SessionSongHistoryKeys.Add(key);
        }
    }

    private static IEnumerable<string> GetSessionHistoryKeys(BLWebUtil.MapEntry map)
    {
        var hasStableKey = false;

        if (map.Hash is string hash && !string.IsNullOrWhiteSpace(hash))
        {
            hasStableKey = true;
            yield return "hash:" + hash.Trim();
        }

        if (map.Id is string id && !string.IsNullOrWhiteSpace(id))
        {
            hasStableKey = true;
            yield return "id:" + id.Trim();
        }

        if (map.DownloadUrl is string downloadUrl && !string.IsNullOrWhiteSpace(downloadUrl))
        {
            hasStableKey = true;
            yield return "download:" + downloadUrl.Trim();
        }

        if (!hasStableKey && map.Name is string name && !string.IsNullOrWhiteSpace(name))
        {
            yield return "name:" + name.Trim() + "|mapper:" + (map.Mapper ?? string.Empty).Trim();
        }
    }

    private static EvaluatedDifficulty? SelectRandomDifficulty(
        IReadOnlyCollection<EvaluatedDifficulty> difficulties)
    {
        var totalWeight = 0d;

        foreach (var evaluatedDifficulty in difficulties)
        {
            if (IsValidWeight(evaluatedDifficulty.Score))
            {
                totalWeight += evaluatedDifficulty.Score;
            }
        }

        if (totalWeight <= 0d) return null;

        double selectionPoint;
        lock (Random)
        {
            selectionPoint = Random.NextDouble() * totalWeight;
        }

        EvaluatedDifficulty? lastEligibleDifficulty = null;

        foreach (var evaluatedDifficulty in difficulties)
        {
            if (!IsValidWeight(evaluatedDifficulty.Score)) continue;

            lastEligibleDifficulty = evaluatedDifficulty;
            selectionPoint -= evaluatedDifficulty.Score;

            if (selectionPoint < 0d)
            {
                return evaluatedDifficulty;
            }
        }

        // Protect against a possible floating-point rounding remainder.
        return lastEligibleDifficulty;
    }

    private static bool IsValidWeight(float score)
    {
        return score > 0f && !float.IsNaN(score) && !float.IsInfinity(score);
    }

    private static List<EvaluatedDifficulty> EvaluateMapDifficulties(
        IEnumerable<BLWebUtil.MapEntry> maps,
        int mapBalance,
        float expectedStars,
        PluginConfig config)
    {
        var evaluatedDifficulties = new List<EvaluatedDifficulty>();

        foreach (var map in maps)
        {
            if (map.Difficulties == null) continue;

            foreach (var difficulty in map.Difficulties)
            {
                var score = CalculateDifficultyScore(
                    map,
                    difficulty,
                    mapBalance,
                    expectedStars,
                    config);

                evaluatedDifficulties.Add(new EvaluatedDifficulty(map, difficulty, score));
            }
        }

        return evaluatedDifficulties;
    }
    
    private static float CalculateDifficultyScore(
        BLWebUtil.MapEntry map,
        BLWebUtil.DifficultyEntry difficulty,
        int mapBalance,
        float expectedStars,
        PluginConfig config)
    {
        // Final rating:
        // 0 - Song is not included in the final selection
        // 1 - Song has the biggest chance of being selected
        
        if (difficulty.PassRating == null || difficulty.TechRating == null || difficulty.Stars == null) 
        {
            return 0f;
        }
        
        // -1 - only Pass; +1 - only Tech
        float style = (difficulty.TechRating.Value - difficulty.PassRating.Value) /
                      (difficulty.TechRating.Value + difficulty.PassRating.Value);
        

        var stylePoints = 0f; // How the song fits the desired balance 0 - 1

        switch (mapBalance)
        {
            case 0: // T E C H
                stylePoints = style switch
                {
                    >= 0.5f => 1f,
                    > 0f    => style / 0.5f,
                    _       => 0f
                };
                break;

            case 1: // Tech
                stylePoints = style switch
                {
                    >= 0.3f => 1f,

                    >= 0f =>
                        0.5f + style / 0.3f * 0.5f,

                    > -0.1f =>
                        (1f + style / 0.1f) * 0.25f,

                    _ => 0f
                };
                break;

            case 2: // Balanced
                stylePoints = Mathf.Clamp01(
                    1f - Mathf.Abs(style) / 0.15f
                );
                break;

            case 3: // Pass
                stylePoints = style switch
                {
                    <= -0.3f => 1f,

                    <= 0f =>
                        0.5f + -style / 0.3f * 0.5f,

                    < 0.1f =>
                        (1f - style / 0.1f) * 0.25f,

                    _ => 0f
                };
                break;

            case 4: // P A S S
                stylePoints = style switch
                {
                    <= -0.5f => 1f,
                    < 0f     => -style / 0.5f,
                    _        => 0f
                };
                break;

            default:
                stylePoints = 0f;
                break;
        }
        
        stylePoints = Mathf.Clamp01(stylePoints); // If something bad happens (it will)

        if (stylePoints == 0f) return 0f;

        float difficultyDiff = Mathf.Abs((difficulty.Stars.Value - expectedStars) /
                                           (difficulty.Stars.Value + expectedStars));
        
        var difficultyPoints = difficultyDiff switch
        {
            <= 1.5f => 1 - difficultyDiff / 1.5f,
            _ => 0f
        };
        
        difficultyPoints = Mathf.Clamp01(difficultyPoints);
        
        
        if (!config.RecommendationsDurationEnabled) return 0.6f * stylePoints + 0.4f * difficultyPoints;

        float durationPoints;
        
        if (map.duration >= config.MinimumRecommendedSongDurationSeconds &&
            map.duration <= config.MaximumRecommendedSongDurationSeconds)
        {
            durationPoints = 1f;
        }
        else
        {
            var distance =
                map.duration < config.MinimumRecommendedSongDurationSeconds
                    ? config.MinimumRecommendedSongDurationSeconds - map.duration
                    : map.duration - config.MaximumRecommendedSongDurationSeconds;

            durationPoints = Mathf.Max(0f, 1f - (distance ?? 15f) / 15f);
        }
        
        return 0.5f * stylePoints + 0.4f * difficultyPoints + 0.1f * durationPoints;

    }
}
