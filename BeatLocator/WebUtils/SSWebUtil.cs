using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using BeatLocator.EvaluationManagers;
using BeatLocator.PostLevel;
using BeatLocator.Settings;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace BeatLocator.WebUtils;

internal static class SSWebUtil
{
    private const string ScoreSaberApiBaseUrl = "https://scoresaber.com/api/v2";
    private const string BeatSaverApiBaseUrl = "https://api.beatsaver.com";
    private const int MaximumApiPageSize = 100;
    private const int BeatSaverBatchSize = 50;
    private const int MaximumPlayedScorePages = 3;
    private const int ScoreCheckRetryCount = 2;
    private const int ScoreCheckSpacingMilliseconds = 250;
    private const int PostLevelScorePollMilliseconds = 1500;
    private const int ProfileSettlementPollMilliseconds = 1250;
    private const int ProfileSettlementTimeoutSeconds = 10;
    private const double ProfileGainEpsilon = 0.0001d;
    internal const int MaximumNewScoreChecks = 120;
    private static readonly HttpClient HttpClient = CreateHttpClient();
    private static readonly SemaphoreSlim ScoreCheckGate = new SemaphoreSlim(1, 1);
    private static readonly object ScoreStatusCacheLock = new object();
    private static readonly object RandomLock = new object();
    private static readonly Random Random = new Random();
    private static readonly Dictionary<string, bool> ScoreStatusCache =
        new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
    private static DateTimeOffset _nextScoreCheckAt = DateTimeOffset.MinValue;

    internal static string? UserId { get; private set; }

    internal sealed class SearchContext
    {
        internal List<ScoreSaberLeaderboard>? PlayedLeaderboards { get; set; }

        internal SearchContext(ScoreSaberPlayedFilter playedFilter)
        {
            PlayedFilter = playedFilter;
        }

        internal ScoreSaberPlayedFilter PlayedFilter { get; }

        internal int NewScoreChecks { get; set; }
    }

    internal static SearchContext CreateSearchContext(
        ScoreSaberPlayedFilter playedFilter)
    {
        return new SearchContext(playedFilter);
    }

    internal static async Task<ProviderPpBaseline?> CapturePpBaselineAsync(
        IPlatformUserModel platformUserModel,
        EvaluatedDifficulty selection,
        CancellationToken cancellationToken)
    {
        try
        {
            var userInfo = await platformUserModel.GetUserInfo(cancellationToken);
            if (string.IsNullOrWhiteSpace(userInfo.platformUserId)) return null;
            var playerId = userInfo.platformUserId!;
            UserId = playerId;

            var profile = await GetJsonAsync<ScoreSaberBasicProfile>(
                AddCacheBuster(
                    ScoreSaberApiBaseUrl +
                    $"/players/{Uri.EscapeDataString(playerId)}/basic"),
                cancellationToken);
            var recentScores = await FetchRecentPpScoresAsync(
                playerId,
                MaximumApiPageSize,
                cancellationToken,
                bypassCache: true);
            var personalBest = recentScores.Find(score =>
                IsMatchingScore(score, selection, null, null));
            Plugin.Log.Info(
                $"[PP] ScoreSaber baseline captured: total_pp=" +
                $"{profile.Stats?.TotalPp?.ToString("0.0000", CultureInfo.InvariantCulture) ?? "unavailable"}, " +
                $"map_pb={personalBest?.Score?.Pp.ToString("0.00", CultureInfo.InvariantCulture) ?? "none"}.");
            return new ProviderPpBaseline
            {
                PlayerId = playerId,
                TotalPp = profile.Stats?.TotalPp,
                PersonalBestModifiedScore = personalBest?.Score?.ModifiedScore,
                PersonalBestPp = personalBest?.Score?.Pp,
                CapturedAt = DateTimeOffset.UtcNow
            };
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            Plugin.Log.Warn($"[PP] Could not capture ScoreSaber PP baseline: {exception.Message}");
            return null;
        }
    }

    internal static async Task<ProviderPpResult> ResolvePpAsync(
        EvaluatedDifficulty selection,
        ProviderPpBaseline? baseline,
        DateTimeOffset launchedAt,
        int modifiedScore,
        CancellationToken cancellationToken)
    {
        if (baseline?.PersonalBestModifiedScore >= modifiedScore)
        {
            return new ProviderPpResult
            {
                Outcome = PpResolutionOutcome.NonPersonalBest,
                Detail = $"Current attempt {modifiedScore} did not beat the pre-play PB " +
                         $"{baseline.PersonalBestModifiedScore}."
            };
        }

        var playerId = baseline?.PlayerId ?? UserId;
        if (string.IsNullOrWhiteSpace(playerId))
        {
            return new ProviderPpResult
            {
                Outcome = PpResolutionOutcome.UploadFailed,
                Detail = "ScoreSaber player ID is unavailable."
            };
        }
        var resolvedPlayerId = playerId!;

        var deadline = DateTimeOffset.UtcNow.AddSeconds(120);
        while (DateTimeOffset.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var recentScores = await FetchRecentPpScoresAsync(
                resolvedPlayerId,
                MaximumApiPageSize,
                cancellationToken,
                bypassCache: true);
            var uploadedScore = recentScores.Find(score =>
                IsMatchingScore(score, selection, launchedAt, modifiedScore));
            if (uploadedScore?.Score != null)
            {
                Plugin.Log.Info(
                    $"[PP] ScoreSaber score {uploadedScore.Score.Id} is visible; " +
                    $"waiting up to {ProfileSettlementTimeoutSeconds} seconds for totalPP settlement.");
                var profileGain = await WaitForProfileGainAsync(
                    resolvedPlayerId,
                    baseline?.TotalPp,
                    cancellationToken);
                return new ProviderPpResult
                {
                    Outcome = PpResolutionOutcome.UploadedNewBest,
                    ScorePp = uploadedScore.Score.Pp,
                    ProfileGain = profileGain,
                    Detail = $"score_id={uploadedScore.Score.Id}; profile gain is measured from " +
                             "the before/after ScoreSaber totalPP snapshots"
                };
            }

            await Task.Delay(PostLevelScorePollMilliseconds, cancellationToken);
        }

        return new ProviderPpResult
        {
            Outcome = PpResolutionOutcome.TimedOut,
            Detail = "The matching ScoreSaber personal best did not appear within 120 seconds."
        };
    }

    private static async Task<List<ScoreSaberScoreEntry>> FetchRecentPpScoresAsync(
        string playerId,
        int limit,
        CancellationToken cancellationToken,
        bool bypassCache = false)
    {
        var uri = ScoreSaberApiBaseUrl +
                  $"/players/{Uri.EscapeDataString(playerId)}/scores" +
                  $"?page=1&limit={limit}&sort=recent";
        if (bypassCache) uri = AddCacheBuster(uri);
        var response = await GetJsonAsync<ScoreSaberScoresResponse>(uri, cancellationToken);
        return response.Data ?? new List<ScoreSaberScoreEntry>();
    }

    private static async Task<double?> WaitForProfileGainAsync(
        string playerId,
        double? baselineTotalPp,
        CancellationToken cancellationToken)
    {
        if (!baselineTotalPp.HasValue) return null;

        var deadline = DateTimeOffset.UtcNow.AddSeconds(ProfileSettlementTimeoutSeconds);
        double? lastGain = null;
        do
        {
            var profile = await GetJsonAsync<ScoreSaberBasicProfile>(
                AddCacheBuster(
                    ScoreSaberApiBaseUrl +
                    $"/players/{Uri.EscapeDataString(playerId)}/basic"),
                cancellationToken);
            if (profile.Stats?.TotalPp.HasValue == true)
            {
                lastGain = profile.Stats.TotalPp.Value - baselineTotalPp.Value;
                if (Math.Abs(lastGain.Value) >= ProfileGainEpsilon)
                {
                    Plugin.Log.Info(
                        $"[PP] ScoreSaber totalPP settled: profile_gain=" +
                        $"{lastGain.Value:+0.0000;-0.0000;0.0000}.");
                    return lastGain;
                }
            }

            if (DateTimeOffset.UtcNow >= deadline) break;
            await Task.Delay(ProfileSettlementPollMilliseconds, cancellationToken);
        } while (DateTimeOffset.UtcNow < deadline);

        Plugin.Log.Warn(
            $"[PP] ScoreSaber totalPP remained unchanged for " +
            $"{ProfileSettlementTimeoutSeconds} seconds after the score became visible.");
        return lastGain;
    }

    private static string AddCacheBuster(string uri)
    {
        var separator = uri.IndexOf('?') >= 0 ? "&" : "?";
        return uri + separator + "_beatlocator=" +
               DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
    }

    private static bool IsMatchingScore(
        ScoreSaberScoreEntry entry,
        EvaluatedDifficulty selection,
        DateTimeOffset? minimumCreatedAt,
        int? modifiedScore)
    {
        if (entry.Score == null || !entry.Score.PersonalBest ||
            entry.Leaderboard?.Map == null ||
            !string.Equals(
                entry.Leaderboard.Map.Hash,
                selection.Map.Hash,
                StringComparison.OrdinalIgnoreCase) ||
            modifiedScore.HasValue && entry.Score.ModifiedScore != modifiedScore.Value ||
            !string.Equals(
                NormalizeDifficulty(entry.Leaderboard.Difficulty?.Difficulty),
                NormalizeDifficultyName(selection.Difficulty.DifficultyName),
                StringComparison.OrdinalIgnoreCase) ||
            !ModesMatch(
                entry.Leaderboard.Difficulty?.GameMode,
                selection.Difficulty.ModeName))
        {
            return false;
        }

        if (!minimumCreatedAt.HasValue) return true;
        return DateTimeOffset.TryParse(
                   entry.Score.CreatedAt,
                   CultureInfo.InvariantCulture,
                   DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                   out var createdAt) &&
               createdAt >= minimumCreatedAt.Value.AddSeconds(-10);
    }

    private static string NormalizeDifficulty(int? difficulty)
    {
        return difficulty.HasValue
            ? GetDifficultyName(difficulty.Value, null).Replace("+", "plus").ToLowerInvariant()
            : string.Empty;
    }

    private static string NormalizeDifficultyName(string? difficulty)
    {
        return (difficulty ?? string.Empty).Replace("+", "plus")
            .Replace("_", string.Empty)
            .ToLowerInvariant();
    }

    private static bool ModesMatch(string? actual, string? expected)
    {
        var actualNormalized = (actual ?? string.Empty).ToLowerInvariant();
        var expectedNormalized = (expected ?? string.Empty).ToLowerInvariant();
        if (actualNormalized.Contains("standard") && expectedNormalized.Contains("standard"))
        {
            return true;
        }

        return actualNormalized == expectedNormalized;
    }

    internal static async Task<List<PlayerScoreEntry>?> FetchCurrentUserScoresAsync(
        IPlatformUserModel platformUserModel,
        int playNumber,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var userInfo = await platformUserModel.GetUserInfo(cancellationToken);
            if (string.IsNullOrWhiteSpace(userInfo.platformUserId))
            {
                Plugin.Log.Warn("Beat Saber did not provide a platform user ID for ScoreSaber.");
                return null;
            }

            UserId = userInfo.platformUserId;
            var limit = Math.Min(Math.Max(playNumber, 1), MaximumApiPageSize);
            var uri = ScoreSaberApiBaseUrl +
                      $"/players/{Uri.EscapeDataString(UserId)}/scores?page=1&limit={limit}&sort=recent";
            var response = await GetJsonAsync<ScoreSaberScoresResponse>(uri, cancellationToken);
            var scores = new List<PlayerScoreEntry>();

            foreach (var entry in response.Data ?? Enumerable.Empty<ScoreSaberScoreEntry>())
            {
                var stars = entry.Leaderboard?.Realm?.Stars;
                var score = entry.Score;
                if (!stars.HasValue || stars.Value <= 0f ||
                    score == null ||
                    !string.Equals(score.PlayOutcome, "CLEAR", StringComparison.OrdinalIgnoreCase) ||
                    !DateTimeOffset.TryParse(
                        score.CreatedAt,
                        CultureInfo.InvariantCulture,
                        DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                        out var playedAt))
                {
                    continue;
                }

                scores.Add(new PlayerScoreEntry
                {
                    Accuracy = score.Accuracy,
                    Timepost = checked((int)playedAt.ToUnixTimeSeconds()),
                    Leaderboard = new ScoreLeaderboard
                    {
                        Difficulty = new ScoreDifficulty
                        {
                            Stars = stars.Value
                        }
                    }
                });
            }

            return scores.Count == 0 ? null : scores;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            Plugin.Log.Error($"Could not request ScoreSaber scores: {exception}");
            return null;
        }
    }

    internal static async Task<List<RecommendationMap>?> FindMapAsync(
        float expectedStars,
        float starBuffer,
        bool onlyTwoSaber,
        int mapDifficulty,
        SongDurationFilter durationFilter,
        int count,
        SearchContext searchContext,
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(UserId))
            {
                throw new InvalidOperationException(
                    "ScoreSaber player ID is unavailable. Load the player score history first.");
            }

            var minimumStars = Math.Max(0f, expectedStars - starBuffer);
            var maximumStars = Math.Min(20f, expectedStars + starBuffer);
            var sortBy = mapDifficulty == 0 || mapDifficulty == 4
                ? "stars"
                : "totalScores";
            var sortDirection = mapDifficulty == 0 ? "asc" : "desc";
            var requestedCount = Math.Min(Math.Max(count, 1), MaximumApiPageSize);
            List<ScoreSaberLeaderboard> candidates;
            if (searchContext.PlayedFilter == ScoreSaberPlayedFilter.Played)
            {
                var playedLeaderboards = await LoadPlayedLeaderboardsAsync(
                    searchContext,
                    cancellationToken);
                candidates = TakeRandom(
                    FilterValidLeaderboards(
                        playedLeaderboards,
                        minimumStars,
                        maximumStars,
                        onlyTwoSaber),
                    requestedCount);
            }
            else
            {
                var page = searchContext.PlayedFilter == ScoreSaberPlayedFilter.New
                    ? await PickRandomLeaderboardPageAsync(
                        minimumStars,
                        maximumStars,
                        sortBy,
                        sortDirection,
                        requestedCount,
                        cancellationToken)
                    : 1;
                var response = await FetchLeaderboardPageAsync(
                    page,
                    requestedCount,
                    minimumStars,
                    maximumStars,
                    sortBy,
                    sortDirection,
                    cancellationToken);
                candidates = FilterValidLeaderboards(
                    response.Data ?? Enumerable.Empty<ScoreSaberLeaderboard>(),
                    minimumStars,
                    maximumStars,
                    onlyTwoSaber).ToList();
            }

            if (candidates.Count == 0)
            {
                return new List<RecommendationMap>();
            }

            var mapDetails = await FetchBeatSaverMapsAsync(
                candidates.Select(candidate => candidate.Map!.Hash),
                cancellationToken);
            return ConvertMaps(candidates, mapDetails)
                .Where(map => durationFilter.Matches(map.Duration))
                .ToList();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            Plugin.Log.Error($"Could not find a ScoreSaber map: {exception}");
            return null;
        }
    }

    private static async Task<ScoreSaberLeaderboardsResponse> FetchLeaderboardPageAsync(
        int page,
        int limit,
        float minimumStars,
        float maximumStars,
        string sortBy,
        string sortDirection,
        CancellationToken cancellationToken)
    {
        var uri = ScoreSaberApiBaseUrl + "/leaderboards?" + string.Join("&", new[]
        {
            $"page={page}",
            $"limit={limit}",
            "status=RANKED",
            $"minStars={minimumStars.ToString(CultureInfo.InvariantCulture)}",
            $"maxStars={maximumStars.ToString(CultureInfo.InvariantCulture)}",
            $"sortBy={sortBy}",
            $"sortDirection={sortDirection}"
        });
        return await GetJsonAsync<ScoreSaberLeaderboardsResponse>(uri, cancellationToken);
    }

    private static async Task<int> PickRandomLeaderboardPageAsync(
        float minimumStars,
        float maximumStars,
        string sortBy,
        string sortDirection,
        int requestedCount,
        CancellationToken cancellationToken)
    {
        var metadataResponse = await FetchLeaderboardPageAsync(
            1,
            1,
            minimumStars,
            maximumStars,
            sortBy,
            sortDirection,
            cancellationToken);
        var totalItems = Math.Max(0, metadataResponse.Metadata?.TotalItems ?? 0);
        var totalPages = Math.Max(1, (totalItems + requestedCount - 1) / requestedCount);
        int selectedPage;
        lock (RandomLock)
        {
            selectedPage = Random.Next(1, totalPages + 1);
        }

        Plugin.Log.Debug(
            $"ScoreSaber New filter selected leaderboard page {selectedPage}/{totalPages} " +
            $"from {totalItems} matching difficulties.");
        return selectedPage;
    }

    private static async Task<IEnumerable<ScoreSaberLeaderboard>> LoadPlayedLeaderboardsAsync(
        SearchContext searchContext,
        CancellationToken cancellationToken)
    {
        if (searchContext.PlayedLeaderboards != null)
        {
            return searchContext.PlayedLeaderboards;
        }

        if (string.IsNullOrWhiteSpace(UserId))
        {
            throw new InvalidOperationException(
                "ScoreSaber player ID is unavailable. Load the player score history first.");
        }

        var firstPage = await FetchPlayerScorePageAsync(
            UserId!,
            1,
            MaximumApiPageSize,
            cancellationToken);
        var entries = new List<ScoreSaberScoreEntry>(
            firstPage.Data ?? Enumerable.Empty<ScoreSaberScoreEntry>());
        var totalItems = Math.Max(0, firstPage.Metadata?.TotalItems ?? entries.Count);
        var totalPages = Math.Max(
            1,
            (totalItems + MaximumApiPageSize - 1) / MaximumApiPageSize);
        var usedPages = new HashSet<int> { 1 };

        while (usedPages.Count < Math.Min(MaximumPlayedScorePages, totalPages))
        {
            int page;
            lock (RandomLock)
            {
                do
                {
                    page = Random.Next(2, totalPages + 1);
                } while (usedPages.Contains(page));
            }

            usedPages.Add(page);
            var response = await FetchPlayerScorePageAsync(
                UserId!,
                page,
                MaximumApiPageSize,
                cancellationToken);
            entries.AddRange(response.Data ?? Enumerable.Empty<ScoreSaberScoreEntry>());
        }

        searchContext.PlayedLeaderboards = entries
            .Where(entry =>
                entry.Leaderboard != null &&
                string.Equals(
                    entry.Score?.PlayOutcome,
                    "CLEAR",
                    StringComparison.OrdinalIgnoreCase))
            .Select(entry => entry.Leaderboard!)
            .ToList();
        foreach (var leaderboard in searchContext.PlayedLeaderboards)
        {
            CacheScoreStatus(leaderboard, true);
        }

        Plugin.Log.Debug(
            $"ScoreSaber Played filter loaded {searchContext.PlayedLeaderboards.Count} " +
            $"cleared leaderboard entries from {usedPages.Count} score page(s).");

        return searchContext.PlayedLeaderboards;
    }

    private static Task<ScoreSaberScoresResponse> FetchPlayerScorePageAsync(
        string playerId,
        int page,
        int limit,
        CancellationToken cancellationToken)
    {
        var uri = ScoreSaberApiBaseUrl +
                  $"/players/{Uri.EscapeDataString(playerId)}/scores" +
                  $"?page={page}&limit={limit}&sort=recent";
        return GetJsonAsync<ScoreSaberScoresResponse>(uri, cancellationToken);
    }

    private static List<ScoreSaberLeaderboard> TakeRandom(
        IEnumerable<ScoreSaberLeaderboard> source,
        int count)
    {
        lock (RandomLock)
        {
            return source.OrderBy(_ => Random.Next()).Take(count).ToList();
        }
    }

    private static IEnumerable<ScoreSaberLeaderboard> FilterValidLeaderboards(
        IEnumerable<ScoreSaberLeaderboard?> source,
        float minimumStars,
        float maximumStars,
        bool onlyTwoSaber)
    {
        return source
            .Where(candidate =>
                candidate?.Map != null &&
                !string.IsNullOrWhiteSpace(candidate.Map.Hash) &&
                candidate.Difficulty != null &&
                !string.IsNullOrWhiteSpace(candidate.Difficulty.GameMode) &&
                candidate.Realm?.Stars >= minimumStars &&
                candidate.Realm.Stars <= maximumStars &&
                (!onlyTwoSaber || IsTwoSaberMode(candidate.Difficulty.GameMode)))
            .Select(candidate => candidate!);
    }

    private static async Task<Dictionary<string, BeatSaverMapDetail>> FetchBeatSaverMapsAsync(
        IEnumerable<string?> sourceHashes,
        CancellationToken cancellationToken)
    {
        var hashes = sourceHashes
            .Where(hash => !string.IsNullOrWhiteSpace(hash))
            .Select(hash => hash!.ToLowerInvariant())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var maps = new Dictionary<string, BeatSaverMapDetail>(
            StringComparer.OrdinalIgnoreCase);

        for (var index = 0; index < hashes.Count; index += BeatSaverBatchSize)
        {
            var batch = hashes.Skip(index).Take(BeatSaverBatchSize).ToList();
            var uri = BeatSaverApiBaseUrl + "/maps/hash/" + string.Join(",", batch);
            var json = await GetJsonStringAsync(uri, cancellationToken);
            var token = JToken.Parse(json);

            if (batch.Count == 1 && token["id"] != null)
            {
                var detail = token.ToObject<BeatSaverMapDetail>();
                if (detail != null) maps[batch[0]] = detail;
                continue;
            }

            foreach (var property in token.Children<JProperty>())
            {
                if (property.Value.Type == JTokenType.Null) continue;
                var detail = property.Value.ToObject<BeatSaverMapDetail>();
                if (detail != null) maps[property.Name] = detail;
            }
        }

        return maps;
    }

    private static List<RecommendationMap> ConvertMaps(
        IEnumerable<ScoreSaberLeaderboard> candidates,
        IReadOnlyDictionary<string, BeatSaverMapDetail> mapDetails)
    {
        var maps = new List<RecommendationMap>();

        foreach (var group in candidates.GroupBy(
                     candidate => candidate.Map!.Hash!,
                     StringComparer.OrdinalIgnoreCase))
        {
            var first = group.First();
            var scoreSaberMap = first.Map!;
            mapDetails.TryGetValue(scoreSaberMap.Hash!, out var beatSaverMap);
            var version = beatSaverMap?.Versions?.FirstOrDefault(candidate =>
                string.Equals(candidate.Hash, scoreSaberMap.Hash, StringComparison.OrdinalIgnoreCase));
            var metadata = beatSaverMap?.Metadata;

            var map = new RecommendationMap
            {
                Id = scoreSaberMap.Bsid ?? beatSaverMap?.Id ?? scoreSaberMap.Id.ToString(CultureInfo.InvariantCulture),
                Hash = scoreSaberMap.Hash,
                Name = scoreSaberMap.SongName ?? metadata?.SongName,
                SubName = scoreSaberMap.SongSubName ?? metadata?.SongSubName,
                Author = scoreSaberMap.SongAuthorName ?? metadata?.SongAuthorName,
                Mapper = scoreSaberMap.LevelAuthorName ?? metadata?.LevelAuthorName,
                CoverImage = scoreSaberMap.CoverUrl ?? version?.CoverUrl,
                FullCoverImage = version?.CoverUrl ?? scoreSaberMap.CoverUrl,
                DownloadUrl = version?.DownloadUrl,
                Duration = metadata?.Duration,
                Difficulties = new List<RecommendationDifficulty>()
            };

            foreach (var leaderboard in group)
            {
                var difficulty = leaderboard.Difficulty!;
                map.Difficulties.Add(new RecommendationDifficulty
                {
                    DifficultyName = GetDifficultyName(difficulty.Difficulty, difficulty.RawDifficulty),
                    ModeName = difficulty.GameMode,
                    Stars = leaderboard.Realm!.Stars,
                    ProviderDifficultyId = difficulty.Difficulty,
                    RealmId = leaderboard.Realm.Id
                });
            }

            maps.Add(map);
        }

        return maps;
    }

    private static string GetDifficultyName(int difficulty, string? rawDifficulty)
    {
        return difficulty switch
        {
            1 => "Easy",
            3 => "Normal",
            5 => "Hard",
            7 => "Expert",
            9 => "ExpertPlus",
            _ => rawDifficulty?.Split('_').FirstOrDefault(part => part.Length > 0) ??
                 difficulty.ToString(CultureInfo.InvariantCulture)
        };
    }

    private static bool IsTwoSaberMode(string? gameMode)
    {
        return gameMode != null &&
               gameMode.IndexOf("Standard", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    internal static async Task<bool> HasCurrentUserClearedAsync(
        EvaluatedDifficulty candidate,
        SearchContext searchContext,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(UserId))
        {
            throw new InvalidOperationException("ScoreSaber player ID is unavailable.");
        }

        var hash = candidate.Map.Hash;
        var mode = candidate.Difficulty.ModeName;
        var difficulty = candidate.Difficulty.ProviderDifficultyId;
        var realmId = candidate.Difficulty.RealmId;
        if (string.IsNullOrWhiteSpace(hash) || string.IsNullOrWhiteSpace(mode) ||
            !difficulty.HasValue)
        {
            throw new InvalidOperationException(
                "The ScoreSaber candidate does not contain a hash, mode, or difficulty ID.");
        }

        var cacheKey = CreateScoreStatusKey(
            UserId!,
            hash!,
            mode!,
            difficulty.Value,
            realmId);
        lock (ScoreStatusCacheLock)
        {
            if (ScoreStatusCache.TryGetValue(cacheKey, out var cachedPlayed))
            {
                return cachedPlayed;
            }
        }

        await ScoreCheckGate.WaitAsync(cancellationToken);
        try
        {
            lock (ScoreStatusCacheLock)
            {
                if (ScoreStatusCache.TryGetValue(cacheKey, out var cachedPlayed))
                {
                    return cachedPlayed;
                }
            }

            searchContext.NewScoreChecks++;

            for (var attempt = 0; attempt <= ScoreCheckRetryCount; attempt++)
            {
                var wait = _nextScoreCheckAt - DateTimeOffset.UtcNow;
                if (wait > TimeSpan.Zero)
                {
                    await Task.Delay(wait, cancellationToken);
                }

                var uri = ScoreSaberApiBaseUrl +
                          $"/players/{Uri.EscapeDataString(UserId!)}/scores/hash/" +
                          $"{Uri.EscapeDataString(hash!)}/{Uri.EscapeDataString(mode!)}/" +
                          difficulty.Value.ToString(CultureInfo.InvariantCulture) +
                          (realmId.HasValue
                              ? $"?realmId={realmId.Value.ToString(CultureInfo.InvariantCulture)}"
                              : string.Empty);
                using var request = new HttpRequestMessage(HttpMethod.Get, uri);
                using var response = await HttpClient.SendAsync(request, cancellationToken);
                var responseBody = await response.Content.ReadAsStringAsync();
                cancellationToken.ThrowIfCancellationRequested();
                _nextScoreCheckAt = DateTimeOffset.UtcNow.AddMilliseconds(
                    ScoreCheckSpacingMilliseconds);

                if (response.StatusCode == HttpStatusCode.NotFound)
                {
                    SetCachedScoreStatus(cacheKey, false);
                    LogScoreCheck(candidate, searchContext.NewScoreChecks, "new");
                    return false;
                }

                if (response.IsSuccessStatusCode)
                {
                    var score = JsonConvert.DeserializeObject<ScoreSaberScore>(responseBody)
                                ?? throw new JsonException(
                                    "ScoreSaber returned an empty player score response.");
                    var played = string.Equals(
                        score.PlayOutcome,
                        "CLEAR",
                        StringComparison.OrdinalIgnoreCase);
                    SetCachedScoreStatus(cacheKey, played);
                    LogScoreCheck(
                        candidate,
                        searchContext.NewScoreChecks,
                        played ? "played" : $"new ({score.PlayOutcome ?? "unknown outcome"})");
                    return played;
                }

                if ((int)response.StatusCode == 429 && attempt < ScoreCheckRetryCount)
                {
                    await Task.Delay(GetRateLimitDelay(response), cancellationToken);
                    continue;
                }

                throw new HttpRequestException(
                    $"Request to {uri} returned {(int)response.StatusCode} " +
                    $"({response.ReasonPhrase}): {responseBody}");
            }

            throw new HttpRequestException(
                "ScoreSaber rate-limited the played-map check repeatedly.");
        }
        finally
        {
            ScoreCheckGate.Release();
        }
    }

    private static TimeSpan GetRateLimitDelay(HttpResponseMessage response)
    {
        if (response.Headers.RetryAfter?.Delta is TimeSpan retryAfter &&
            retryAfter > TimeSpan.Zero)
        {
            return retryAfter;
        }

        if (response.Headers.TryGetValues("X-RateLimit-Reset-Short", out var values) &&
            double.TryParse(
                values.FirstOrDefault(),
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out var seconds) &&
            seconds > 0d)
        {
            return TimeSpan.FromSeconds(Math.Min(seconds, 10d));
        }

        return TimeSpan.FromSeconds(1d);
    }

    private static void LogScoreCheck(
        EvaluatedDifficulty candidate,
        int checkNumber,
        string result)
    {
        Plugin.Log.Debug(
            $"ScoreSaber New check {checkNumber}/{MaximumNewScoreChecks}: " +
            $"{candidate.Map.Hash ?? "<no hash>"} " +
            $"{candidate.Difficulty.ModeName ?? "<no mode>"}/" +
            $"{candidate.Difficulty.ProviderDifficultyId?.ToString(CultureInfo.InvariantCulture) ?? "?"} " +
            $"=> {result}.");
    }

    private static void CacheScoreStatus(
        ScoreSaberLeaderboard leaderboard,
        bool played)
    {
        if (string.IsNullOrWhiteSpace(UserId) ||
            string.IsNullOrWhiteSpace(leaderboard.Map?.Hash) ||
            string.IsNullOrWhiteSpace(leaderboard.Difficulty?.GameMode))
        {
            return;
        }

        var key = CreateScoreStatusKey(
            UserId!,
            leaderboard.Map!.Hash!,
            leaderboard.Difficulty!.GameMode!,
            leaderboard.Difficulty.Difficulty,
            leaderboard.Realm?.Id);
        SetCachedScoreStatus(key, played);
    }

    private static string CreateScoreStatusKey(
        string playerId,
        string hash,
        string mode,
        int difficulty,
        int? realmId)
    {
        return playerId.Trim() + "|" + hash.Trim() + "|" + mode.Trim() + "|" +
               difficulty.ToString(CultureInfo.InvariantCulture) + "|" +
               (realmId?.ToString(CultureInfo.InvariantCulture) ?? "active");
    }

    private static void SetCachedScoreStatus(string key, bool played)
    {
        lock (ScoreStatusCacheLock)
        {
            ScoreStatusCache[key] = played;
        }
    }

    private static async Task<T> GetJsonAsync<T>(
        string uri,
        CancellationToken cancellationToken)
    {
        var json = await GetJsonStringAsync(uri, cancellationToken);
        return JsonConvert.DeserializeObject<T>(json)
               ?? throw new JsonException($"The response from {uri} was empty or invalid.");
    }

    private static async Task<string> GetJsonStringAsync(
        string uri,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        using var response = await HttpClient.SendAsync(request, cancellationToken);
        var responseBody = await response.Content.ReadAsStringAsync();
        cancellationToken.ThrowIfCancellationRequested();
        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException(
                $"Request to {uri} returned {(int)response.StatusCode} " +
                $"({response.ReasonPhrase}): {responseBody}");
        }

        return responseBody;
    }

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient();
        client.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "BeatLocator/0.0.2");
        client.DefaultRequestHeaders.TryAddWithoutValidation("Accept", "application/json");
        return client;
    }
    
    #region SCORE JSON

    private sealed class ScoreSaberScoresResponse
    {
        [JsonProperty("data")]
        public List<ScoreSaberScoreEntry>? Data { get; set; }

        [JsonProperty("metadata")]
        public ScoreSaberMetadata? Metadata { get; set; }
    }

    private sealed class ScoreSaberScoreEntry
    {
        [JsonProperty("score")]
        public ScoreSaberScore? Score { get; set; }

        [JsonProperty("leaderboard")]
        public ScoreSaberLeaderboard? Leaderboard { get; set; }
    }

    private sealed class ScoreSaberScore
    {
        [JsonProperty("id")]
        public long Id { get; set; }

        [JsonProperty("modifiedScore")]
        public int ModifiedScore { get; set; }

        [JsonProperty("pp")]
        public double Pp { get; set; }

        [JsonProperty("personalBest")]
        public bool PersonalBest { get; set; }

        [JsonProperty("accuracy")]
        public float Accuracy { get; set; }

        [JsonProperty("playOutcome")]
        public string? PlayOutcome { get; set; }

        [JsonProperty("createdAt")]
        public string? CreatedAt { get; set; }
    }
    
    #endregion
    
    #region PROFILE JSON

    private sealed class ScoreSaberBasicProfile
    {
        [JsonProperty("stats")]
        public ScoreSaberProfileStats? Stats { get; set; }
    }

    private sealed class ScoreSaberProfileStats
    {
        [JsonProperty("totalPP")]
        public double? TotalPp { get; set; }
    }

    private sealed class ScoreSaberLeaderboardsResponse
    {
        [JsonProperty("data")]
        public List<ScoreSaberLeaderboard>? Data { get; set; }

        [JsonProperty("metadata")]
        public ScoreSaberMetadata? Metadata { get; set; }
    }

    internal sealed class ScoreSaberLeaderboard
    {
        [JsonProperty("map")]
        public ScoreSaberMap? Map { get; set; }

        [JsonProperty("difficulty")]
        public ScoreSaberDifficulty? Difficulty { get; set; }

        [JsonProperty("realm")]
        public ScoreSaberRealm? Realm { get; set; }

        [JsonProperty("totalScores")]
        public int TotalScores { get; set; }
    }
    
    #endregion

    #region MAP JSON
    
    internal sealed class ScoreSaberMap
    {
        [JsonProperty("id")]
        public long Id { get; set; }

        [JsonProperty("hash")]
        public string? Hash { get; set; }

        [JsonProperty("bsid")]
        public string? Bsid { get; set; }

        [JsonProperty("songName")]
        public string? SongName { get; set; }

        [JsonProperty("songSubName")]
        public string? SongSubName { get; set; }

        [JsonProperty("songAuthorName")]
        public string? SongAuthorName { get; set; }

        [JsonProperty("levelAuthorName")]
        public string? LevelAuthorName { get; set; }

        [JsonProperty("coverUrl")]
        public string? CoverUrl { get; set; }
    }

    internal sealed class ScoreSaberDifficulty
    {
        [JsonProperty("difficulty")]
        public int Difficulty { get; set; }

        [JsonProperty("rawDifficulty")]
        public string? RawDifficulty { get; set; }

        [JsonProperty("gameMode")]
        public string? GameMode { get; set; }
    }

    internal sealed class ScoreSaberRealm
    {
        [JsonProperty("realmId")]
        public int? Id { get; set; }

        [JsonProperty("stars")]
        public float Stars { get; set; }
    }

    private sealed class ScoreSaberMetadata
    {
        [JsonProperty("totalItems")]
        public int TotalItems { get; set; }

        [JsonProperty("totalPages")]
        public int TotalPages { get; set; }
    }

    private sealed class BeatSaverMapDetail
    {
        [JsonProperty("id")]
        public string? Id { get; set; }

        [JsonProperty("metadata")]
        public BeatSaverMetadata? Metadata { get; set; }

        [JsonProperty("versions")]
        public List<BeatSaverVersion>? Versions { get; set; }
    }

    private sealed class BeatSaverMetadata
    {
        [JsonProperty("duration")]
        public int Duration { get; set; }

        [JsonProperty("songName")]
        public string? SongName { get; set; }

        [JsonProperty("songSubName")]
        public string? SongSubName { get; set; }

        [JsonProperty("songAuthorName")]
        public string? SongAuthorName { get; set; }

        [JsonProperty("levelAuthorName")]
        public string? LevelAuthorName { get; set; }
    }
    
    #endregion

    private sealed class BeatSaverVersion
    {
        [JsonProperty("hash")]
        public string? Hash { get; set; }

        [JsonProperty("downloadURL")]
        public string? DownloadUrl { get; set; }

        [JsonProperty("coverURL")]
        public string? CoverUrl { get; set; }
    }
}
