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

    private const float MinimumTargetStars = PlayerSkillEstimator.MinimumStars;
    private const float MaximumTargetStars = PlayerSkillEstimator.MaximumStars;
    private const float StarBufferStep = 0.5f;
    private const float MaximumAdaptiveStarBuffer = 2f;
    private static readonly System.Random Random = new System.Random();
    private static readonly List<RecommendationMap> SessionSongHistoryEntries =
        new List<RecommendationMap>();
    private static readonly HashSet<string> SessionSongHistoryKeys =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<RankingProvider, List<PlayerScoreEntry>> ProfileScores =
        new Dictionary<RankingProvider, List<PlayerScoreEntry>>();
    private static readonly object ProfileSync = new object();
    private static readonly Dictionary<RankingProvider, PlayerSkillEstimator.Profile> PlayerProfiles = new();
    private static readonly Dictionary<RankingProvider, List<PlayerScoreEntry>> LocalScores = new();
    private static readonly Dictionary<RankingProvider, string> ProfilePlayers = new();

    internal delegate Task<List<PlayerScoreEntry>?> ScoreLoader(
        CancellationToken cancellationToken);

    internal delegate Task<MapSampleResult> MapLoader(
        float expectedStars,
        float starBuffer,
        CancellationToken cancellationToken);

    internal delegate float DifficultyScorer(
        RecommendationMap map,
        RecommendationDifficulty difficulty,
        float expectedStars,
        PluginConfig config);

    internal delegate Task<CandidateFilterResult> CandidateFilter(
        EvaluatedDifficulty difficulty,
        CancellationToken cancellationToken);

    internal readonly struct MapSampleResult
    {
        internal MapSampleResult(
            List<RecommendationMap>? maps,
            bool hasAdditionalSample)
        {
            Maps = maps;
            HasAdditionalSample = hasAdditionalSample;
        }

        internal List<RecommendationMap>? Maps { get; }
        internal bool HasAdditionalSample { get; }
    }

    internal readonly struct CandidateFilterResult
    {
        private CandidateFilterResult(bool accepted, string? failureReason)
        {
            Accepted = accepted;
            FailureReason = failureReason;
        }

        internal bool Accepted { get; }
        internal string? FailureReason { get; }

        internal static CandidateFilterResult Accept()
        {
            return new CandidateFilterResult(true, null);
        }

        internal static CandidateFilterResult Reject()
        {
            return new CandidateFilterResult(false, null);
        }

        internal static CandidateFilterResult Failure(string reason)
        {
            return new CandidateFilterResult(false, reason);
        }
    }

    private readonly struct CandidateSelectionResult
    {
        internal CandidateSelectionResult(
            EvaluatedDifficulty? difficulty,
            string? failureReason)
        {
            Difficulty = difficulty;
            FailureReason = failureReason;
        }

        internal EvaluatedDifficulty? Difficulty { get; }
        internal string? FailureReason { get; }
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
        CandidateFilter? filterCandidateAsync,
        int samplesPerStarBuffer,
        CancellationToken cancellationToken = default,
        MapLoader? loadChallengeCatalogueAsync = null)
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

        if (mapDifficulty == 4 && loadChallengeCatalogueAsync != null)
            return await FindCatalogueChallengeAsync(provider, expectedStars, config, loadChallengeCatalogueAsync,
                scoreDifficulty, filterCandidateAsync, filterDescription, cancellationToken);

        var maximumUsefulBuffer = Math.Max(
            expectedStars - MinimumTargetStars,
            MaximumTargetStars - expectedStars);
        var maximumStarBuffer = Math.Min(
            Math.Min(MaximumAdaptiveStarBuffer, Math.Max(0.15f, expectedStars * 0.3f)),
            maximumUsefulBuffer);
        var currentStarBuffer = Math.Min(
            Math.Max(0f, starBuffer),
            maximumStarBuffer);
        var sampleLimit = Math.Max(1, samplesPerStarBuffer);
        var totalProviderMaps = 0;
        var totalAvailableMaps = 0;

        while (true)
        {
            for (var sample = 1; sample <= sampleLimit; sample++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var mapSample = await loadMapsAsync(
                    expectedStars,
                    currentStarBuffer,
                    cancellationToken);
                var requestedMaps = mapSample.Maps;
                if (requestedMaps == null)
                {
                    var serviceName = provider.GetDisplayName();
                    return MapSearchResult.Failure(
                        $"{serviceName} could not find a ranked map matching your current settings. " +
                        $"Try changing the {filterDescription}. " +
                        "If the search still fails with broad settings, check your sign-in and internet connection.");
                }

                var availableMaps = ExcludeSessionHistory(requestedMaps);
                totalProviderMaps += requestedMaps.Count;
                totalAvailableMaps += availableMaps.Count;

                var evaluatedDifficulties = EvaluateMapDifficulties(
                    availableMaps,
                    expectedStars,
                    config,
                    scoreDifficulty);
                var positiveDifficultyCount = evaluatedDifficulties.Count(
                    difficulty => IsValidWeight(difficulty.Score));
                Plugin.Log.Debug(
                    $"{provider.GetDisplayName()} search sample {sample}/{sampleLimit} at " +
                    $"{expectedStars:0.##} ± {currentStarBuffer:0.#} stars: " +
                    $"provider_maps={requestedMaps.Count}, " +
                    $"after_session_history={availableMaps.Count}, " +
                    $"evaluated_difficulties={evaluatedDifficulties.Count}, " +
                    $"positive_difficulties={positiveDifficultyCount}.");
                var selectionResult = await SelectRandomDifficultyAsync(
                    evaluatedDifficulties,
                    filterCandidateAsync,
                    cancellationToken);
                if (selectionResult.FailureReason != null)
                {
                    return MapSearchResult.Failure(selectionResult.FailureReason);
                }

                var selectedDifficulty = selectionResult.Difficulty;
                if (selectedDifficulty != null)
                {
                    AddToSessionHistory(selectedDifficulty.Map);
                    LogSelection(selectedDifficulty);
                    return MapSearchResult.Success(selectedDifficulty);
                }

                if (!mapSample.HasAdditionalSample)
                {
                    break;
                }
            }

            if (currentStarBuffer >= maximumStarBuffer)
            {
                if (totalProviderMaps > 0 && totalAvailableMaps == 0)
                {
                    return MapSearchResult.Failure(
                        "All matching maps checked by the provider have already been shown " +
                        "during this Beat Saber session. Restart the game or change the filters.");
                }

                Plugin.Log.Warn(
                    "No eligible difficulty was found in the sampled provider results " +
                    $"after widening the search up to {maximumStarBuffer:0.#} stars from the " +
                    $"{expectedStars:0.##}-star target.");
                return MapSearchResult.Failure(
                    "No eligible ranked map was found in the provider result pages " +
                    "checked for this attempt. " +
                    $"Try changing the {filterDescription}.");
            }

            currentStarBuffer = Math.Min(
                currentStarBuffer + StarBufferStep,
                maximumStarBuffer);
        }
    }

    internal static void RecalculatePlayerDifficultyRanges(PluginConfig config)
    {
        lock (ProfileSync)
            foreach (var profile in PlayerProfiles)
                LogProfile(profile.Key, profile.Value, config, "Recalculated");
    }

    // Catalogue loader arguments are lower/upper effective-star bounds. Sample only
    // challenging maps and keep account skill unchanged when the catalogue saturates.
    private static async Task<MapSearchResult> FindCatalogueChallengeAsync(RankingProvider provider,
        float desired, PluginConfig config, MapLoader catalogue, DifficultyScorer scorer,
        CandidateFilter? filter, string filterDescription, CancellationToken token)
    {
        float floor;
        lock (ProfileSync)
            floor = Math.Max(MinimumTargetStars, PlayerProfiles[provider].Center);
        var candidates = new List<EvaluatedDifficulty>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var page = 0; page < 2; page++)
        {
            token.ThrowIfCancellationRequested();
            var upper = Math.Min(MaximumTargetStars, desired + Math.Min(2f, Math.Max(0.15f, desired * 0.3f)));
            var sample = await catalogue(floor, upper, token);
            token.ThrowIfCancellationRequested();
            if (sample.Maps == null) return MapSearchResult.Failure(
                "The ranking provider could not load challenge candidates. Check your connection and sign-in.");
            foreach (var candidate in EvaluateMapDifficulties(ExcludeSessionHistory(sample.Maps), desired, config,
                (map, difficulty, _, settings) => scorer(map, difficulty,
                    difficulty.EvaluationStars ?? difficulty.Stars ?? desired, settings)))
            {
                var stars = candidate.Difficulty.EvaluationStars ?? candidate.Difficulty.Stars ?? 0;
                var key = candidate.Map.Hash + ":" + candidate.Difficulty.ModeName + ":" + candidate.Difficulty.DifficultyName;
                if (stars >= floor && IsValidWeight(candidate.Score) && seen.Add(key)) candidates.Add(candidate);
            }
            if (!sample.HasAdditionalSample) break;
        }
        if (candidates.Count == 0) return MapSearchResult.Failure(
            $"No suitable challenge was found in the sampled ranked catalogue. Try changing the {filterDescription}.");
        float Stars(EvaluatedDifficulty c) => c.Difficulty.EvaluationStars ?? c.Difficulty.Stars ?? 0;
        candidates = candidates.OrderBy(Stars).ToList();
        var ceiling = Stars(candidates[candidates.Count - 1]);
        var target = desired;
        if (target > ceiling)
        {
            // Both modes can saturate. Possible takes the upper part of the available
            // challenge pool; Impossible takes its hardest end, never an easy fallback.
            target = config.isEndMePossible
                ? Stars(candidates[(int)((candidates.Count - 1) * 0.8f)]) : ceiling;
            Plugin.Log.Info($"[Challenge] catalogue limited: desired={desired:0.00}, available_target={target:0.00}, ceiling={ceiling:0.00}; player skill unchanged.");
        }
        // Sparse catalogues can have gaps even below the ceiling.
        target = Stars(candidates.OrderBy(c => Math.Abs(Stars(c) - target)).First());
        Plugin.Log.Info($"[Challenge] desired={desired:0.00}, candidate_target={target:0.00}, candidates={candidates.Count}, floor={floor:0.00}; player skill unchanged.");
        var bandwidth = Math.Min(0.35f, Math.Max(0.06f, target * 0.04f));
        var pool = candidates.Where(c => Math.Abs(Stars(c) - target) <= bandwidth * 0.5f)
            .Select(c => new EvaluatedDifficulty(c.Map, c.Difficulty,
                c.Score / (1f + Math.Abs(Stars(c) - target) / bandwidth))).ToList();
        // Rejecting a candidate (e.g. already played) must not silently lower the challenge.
        var result = await SelectRandomDifficultyAsync(pool, filter, token);
        if (result.FailureReason != null) return MapSearchResult.Failure(result.FailureReason);
        if (result.Difficulty == null) return MapSearchResult.Failure("No available map passed the challenge filters.");
        AddToSessionHistory(result.Difficulty.Map);
        LogSelection(result.Difficulty);
        return MapSearchResult.Success(result.Difficulty);
    }

    internal static void SetProfilePlayer(RankingProvider provider, string playerId)
    {
        lock (ProfileSync)
        {
            if (ProfilePlayers.TryGetValue(provider, out var previous) && previous == playerId) return;
            ProfilePlayers[provider] = playerId;
            ProfileScores.Remove(provider);
            PlayerProfiles.Remove(provider);
            LocalScores.Remove(provider);
        }
    }

    internal static float GetProfileSuitability(RankingProvider provider, RecommendationDifficulty difficulty, float target)
    {
        lock (ProfileSync)
            return PlayerProfiles.TryGetValue(provider, out var profile) ? profile.Suitability(difficulty, target) : 1f;
    }

    internal static bool RecordLocalResult(RankingProvider provider, long runId, EvaluatedDifficulty selection,
        float accuracy, string modifiers, bool failed, DateTimeOffset now)
    {
        if (failed) return false;
        var d = selection.Difficulty;
        var score = new PlayerScoreEntry
        {
            Id = -runId, Accuracy = accuracy, Modifiers = modifiers, Failed = failed, Local = true,
            Timepost = checked((int)now.ToUnixTimeSeconds()),
            Leaderboard = new ScoreLeaderboard
            {
                Song = new ScoreSong { Hash = selection.Map.Hash },
                Difficulty = new ScoreDifficulty
                {
                    Stars = d.Stars, AccRating = d.AccRating, PassRating = d.PassRating,
                    TechRating = d.TechRating, ModifiersRating = d.ModifiersRating,
                    DifficultyName = d.DifficultyName, ModeName = d.ModeName, MaxScore = d.MaxScore
                }
            }
        };
        if (!PlayerSkillEstimator.TryNormalize(score, out _))
        {
            Plugin.Log.Info($"[Skill] {provider.GetDisplayName()} run {runId} excluded: unsupported modifiers/ratings ({modifiers}).");
            return false;
        }
        lock (ProfileSync)
        {
            if (!LocalScores.TryGetValue(provider, out var local)) LocalScores[provider] = local = new();
            if (local.Any(s => s.Id == score.Id)) return false;
            local.Add(score);
            if (local.Count > 32) local.RemoveAt(0);
            if (PlayerProfiles.TryGetValue(provider, out var previous) && ProfileScores.TryGetValue(provider, out var remote))
            {
                var updated = EstimateHistory(provider, remote, now);
                PlayerProfiles[provider] = updated;
                Plugin.Log.Info($"[Skill] {provider.GetDisplayName()} run {runId}: " +
                    $"{(failed ? "failed" : "cleared")}, accuracy={accuracy:P2}, " +
                    $"center={previous.Center:0.00} -> {updated.Center:0.00}; result added to history before PP upload.");
            }
            return true;
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

    internal static float GetMinimumCandidateStars(float target, float buffer, int preset)
    {
        // Widen END ME toward harder maps; do not silently turn it into an easier preset.
        return Math.Max(MinimumTargetStars, preset == 4 ? target : target - buffer);
    }

    private static DifficultyRange ToRange(PlayerSkillEstimator.Profile profile, PluginConfig config)
    {
        return new DifficultyRange(profile.Target(0, config.isEndMePossible),
            profile.Target(1, config.isEndMePossible), profile.Target(2, config.isEndMePossible),
            profile.Target(3, config.isEndMePossible), profile.Target(4, config.isEndMePossible));
    }

    private static async Task<DifficultyProfileResult> GetDifficultyRangeAsync(
        RankingProvider provider, PluginConfig config, ScoreLoader loadScoresAsync,
        CancellationToken cancellationToken)
    {
        // Adapters check player identity and use a short-lived API cache. Never cache skill forever.
        var scores = await loadScoresAsync(cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        if (scores == null)
            return DifficultyProfileResult.Failure($"Could not load your {provider.GetDisplayName()} score history. Check sign-in and connectivity.");
        lock (ProfileSync)
        {
            if (ProfileScores.TryGetValue(provider, out var cached) && ReferenceEquals(cached, scores) &&
                PlayerProfiles.TryGetValue(provider, out var existing))
                return DifficultyProfileResult.Success(ToRange(existing, config));
            try
            {
                var profile = EstimateHistory(provider, scores, DateTimeOffset.UtcNow);
                ProfileScores[provider] = scores;
                PlayerProfiles[provider] = profile;
                LogProfile(provider, profile, config, "Calculated");
                return DifficultyProfileResult.Success(ToRange(profile, config));
            }
            catch (ArgumentException exception)
            {
                Plugin.Log.Warn($"[Skill] {provider.GetDisplayName()} profile unavailable: {exception.Message}");
                return DifficultyProfileResult.Failure($"Could not calculate recommended difficulty: {exception.Message}");
            }
        }
    }

    private static PlayerSkillEstimator.Profile EstimateHistory(RankingProvider provider,
        List<PlayerScoreEntry> remote, DateTimeOffset now)
    {
        LocalScores.TryGetValue(provider, out var local);
        // Remote upload replaces the same local attempt, never an additional sample.
        // Non-PB clears remain ordinary history entries while the session is active.
        var history = local == null ? remote : remote
            .Where(score => !local.Any(attempt => SameAttempt(score, attempt)))
            .Concat(local).ToList();
        return PlayerSkillEstimator.Estimate(history, now);
    }

    private static bool SameAttempt(PlayerScoreEntry remote, PlayerScoreEntry local)
    {
        return !local.Failed && Math.Abs((long)remote.Timepost - local.Timepost) <= 120 &&
            Math.Abs(remote.Accuracy - local.Accuracy) < 0.0001f &&
            string.Equals(LearningSpeed(remote.Modifiers), LearningSpeed(local.Modifiers), StringComparison.Ordinal) &&
            !string.IsNullOrWhiteSpace(local.Leaderboard?.Song?.Hash) &&
            string.Equals(remote.Leaderboard?.Song?.Hash, local.Leaderboard?.Song?.Hash, StringComparison.OrdinalIgnoreCase) &&
            remote.Leaderboard?.Difficulty?.DifficultyName == local.Leaderboard?.Difficulty?.DifficultyName &&
            remote.Leaderboard?.Difficulty?.ModeName == local.Leaderboard?.Difficulty?.ModeName;
    }

    private static string LearningSpeed(string modifiers)
    {
        return modifiers.Split(',').Select(m => m.Trim().ToUpperInvariant())
            .FirstOrDefault(m => m == "FS" || m == "SF" || m == "SS") ?? string.Empty;
    }

    private static void LogProfile(RankingProvider provider, PlayerSkillEstimator.Profile profile, PluginConfig config, string operation)
    {
        var range = ToRange(profile, config);
        Plugin.Log.Info($"{operation} {provider.GetDisplayName()} difficulty stars: " +
            $"{range.Minimum:0.00}, {range.Low:0.00}, {range.Medium:0.00}, {range.High:0.00}, {range.Maximum:0.00}.");
        Plugin.Log.Info($"[Skill] {provider.GetDisplayName()}: samples={profile.SampleCount}, " +
            $"speed_normalized={profile.SpeedCount}, excluded={profile.SkippedCount}, " +
            $"aged_out={profile.AgedOutCount}, " +
            $"confidence={profile.Confidence}, slope={profile.Slope:0.000}, " +
            $"recent_challenge={profile.RecentChallenge:0.00}, challenge_boundary={profile.ChallengeBoundary:0.00}, reference_accuracy={profile.ReferenceAccuracy:P1}, " +
            $"acc/pass/tech={profile.AccBudget:0.00}/{profile.PassBudget:0.00}/{profile.TechBudget:0.00}.");
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

    internal static MapSearchResult SelectFromCandidates(IEnumerable<EvaluatedDifficulty> candidates)
    {
        var available = candidates.Where(candidate => !IsInSessionHistory(candidate.Map)).ToList();
        var selected = SelectRandomDifficulty(available);
        if (selected == null)
        {
            return MapSearchResult.Failure(
                "No BeatSaver difficulty matched your NPS, NJS, style, duration, and quality settings. " +
                "Try a wider range or different filters.");
        }
        AddToSessionHistory(selected.Map);
        Plugin.Log.Info($"Selected BeatSaver map {selected.Map.Id}: " +
                        $"{selected.Difficulty.ModeName}/{selected.Difficulty.DifficultyName}, " +
                        $"NPS={selected.Difficulty.Nps:0.00}, NJS={selected.Difficulty.Njs:0.0}.");
        return MapSearchResult.Success(selected);
    }

    private static List<RecommendationMap> ExcludeSessionHistory(
        IEnumerable<RecommendationMap> maps)
    {
        return maps.Where(map => !IsInSessionHistory(map)).ToList();
    }

    internal static bool IsInSessionHistory(RecommendationMap map)
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

    private static async Task<CandidateSelectionResult> SelectRandomDifficultyAsync(
        IEnumerable<EvaluatedDifficulty> difficulties,
        CandidateFilter? filterCandidateAsync,
        CancellationToken cancellationToken)
    {
        var remaining = difficulties
            .Where(difficulty => IsValidWeight(difficulty.Score))
            .ToList();
        while (remaining.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var candidate = SelectRandomDifficulty(remaining);
            if (candidate == null)
            {
                break;
            }

            if (filterCandidateAsync == null)
            {
                return new CandidateSelectionResult(candidate, null);
            }

            var filterResult = await filterCandidateAsync(candidate, cancellationToken);
            if (filterResult.FailureReason != null)
            {
                return new CandidateSelectionResult(null, filterResult.FailureReason);
            }

            if (filterResult.Accepted)
            {
                return new CandidateSelectionResult(candidate, null);
            }

            remaining.Remove(candidate);
        }

        return new CandidateSelectionResult(null, null);
    }

    private static void LogSelection(EvaluatedDifficulty selectedDifficulty)
    {
        var map = selectedDifficulty.Map;
        var difficulty = selectedDifficulty.Difficulty;
        Plugin.Log.Info(
            $"Selected difficulty: {map.Name ?? "<unnamed map>"} - " +
            $"{difficulty.DifficultyName ?? "<unnamed difficulty>"} " +
            $"({difficulty.ModeName ?? "<unknown mode>"}), " +
            $"{difficulty.EvaluationStars ?? difficulty.Stars:0.00} stars {difficulty.EvaluationSpeed}, " +
            $"score: {selectedDifficulty.Score:0.000}.");
    }

    private static bool IsValidWeight(float score)
    {
        return score > 0f && !float.IsNaN(score) && !float.IsInfinity(score);
    }

}
