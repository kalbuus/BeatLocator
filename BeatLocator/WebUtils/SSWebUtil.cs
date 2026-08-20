using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace BeatLocator.WebUtils;

internal static class SSWebUtil
{
    private const string ScoreSaberApiBaseUrl = "https://scoresaber.com/api/v2";
    private const string BeatSaverApiBaseUrl = "https://api.beatsaver.com";
    private const int MaximumApiPageSize = 100;
    private const int BeatSaverBatchSize = 50;
    private static readonly HttpClient HttpClient = CreateHttpClient();

    internal static string? UserId { get; private set; }

    internal static async Task<List<BLWebUtil.ScoreEntry>?> FetchCurrentUserScoresAsync(
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
            var scores = new List<BLWebUtil.ScoreEntry>();

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

                scores.Add(new BLWebUtil.ScoreEntry
                {
                    Accuracy = score.Accuracy,
                    Timepost = checked((int)playedAt.ToUnixTimeSeconds()),
                    Leaderboard = new BLWebUtil.Leaderboard
                    {
                        Difficulty = new BLWebUtil.DifficultyScoreEntry
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

    internal static async Task<List<BLWebUtil.MapEntry>?> FindMapAsync(
        float expectedStars,
        float starBuffer,
        bool onlyTwoSaber,
        int mapDifficulty,
        int count,
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
            var uri = ScoreSaberApiBaseUrl + "/leaderboards?" + string.Join("&", new[]
            {
                "page=1",
                $"limit={requestedCount}",
                "status=RANKED",
                $"minStars={minimumStars.ToString(CultureInfo.InvariantCulture)}",
                $"maxStars={maximumStars.ToString(CultureInfo.InvariantCulture)}",
                $"sortBy={sortBy}",
                $"sortDirection={sortDirection}"
            });

            var response = await GetJsonAsync<ScoreSaberLeaderboardsResponse>(
                uri,
                cancellationToken);
            var candidates = FilterValidLeaderboards(
                response.Data ?? Enumerable.Empty<ScoreSaberLeaderboard>(),
                minimumStars,
                maximumStars,
                onlyTwoSaber).ToList();

            if (candidates.Count == 0)
            {
                return new List<BLWebUtil.MapEntry>();
            }

            var mapDetails = await FetchBeatSaverMapsAsync(
                candidates.Select(candidate => candidate.Map!.Hash),
                cancellationToken);
            return ConvertMaps(candidates, mapDetails);
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

    private static List<BLWebUtil.MapEntry> ConvertMaps(
        IEnumerable<ScoreSaberLeaderboard> candidates,
        IReadOnlyDictionary<string, BeatSaverMapDetail> mapDetails)
    {
        var maps = new List<BLWebUtil.MapEntry>();

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

            var map = new BLWebUtil.MapEntry
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
                duration = metadata?.Duration,
                Difficulties = new List<BLWebUtil.DifficultyEntry>()
            };

            foreach (var leaderboard in group)
            {
                var difficulty = leaderboard.Difficulty!;
                map.Difficulties.Add(new BLWebUtil.DifficultyEntry
                {
                    DifficultyName = GetDifficultyName(difficulty.Difficulty, difficulty.RawDifficulty),
                    ModeName = difficulty.GameMode,
                    Stars = leaderboard.Realm!.Stars
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
        [JsonProperty("accuracy")]
        public float Accuracy { get; set; }

        [JsonProperty("playOutcome")]
        public string? PlayOutcome { get; set; }

        [JsonProperty("createdAt")]
        public string? CreatedAt { get; set; }
    }

    private sealed class ScoreSaberLeaderboardsResponse
    {
        [JsonProperty("data")]
        public List<ScoreSaberLeaderboard>? Data { get; set; }
    }

    private sealed class ScoreSaberLeaderboard
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

    private sealed class ScoreSaberMap
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

    private sealed class ScoreSaberDifficulty
    {
        [JsonProperty("difficulty")]
        public int Difficulty { get; set; }

        [JsonProperty("rawDifficulty")]
        public string? RawDifficulty { get; set; }

        [JsonProperty("gameMode")]
        public string? GameMode { get; set; }
    }

    private sealed class ScoreSaberRealm
    {
        [JsonProperty("stars")]
        public float Stars { get; set; }
    }

    private sealed class ScoreSaberMetadata
    {
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
