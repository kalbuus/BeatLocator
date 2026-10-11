using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using BeatLocator.EvaluationManagers;
using BeatLocator.Settings;
using Newtonsoft.Json.Linq;

namespace BeatLocator.WebUtils;

internal static class BeatSaverWebUtil
{
    private const string Api = "https://api.beatsaver.com/search/text/";
    private const int PageSize = 20;
    private const int MaximumSamplePages = 3;
    private static readonly Random Random = new Random();
    private static readonly HttpClient Client = CreateClient();
    private static readonly HashSet<string> PlayableModes = new HashSet<string>(StringComparer.Ordinal)
    {
        "Standard", "OneSaber", "NoArrows", "90Degree", "360Degree", "Lawless", "Legacy"
    };

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("BeatLocator/0.1 (BeatSaver search)");
        return client;
    }

    internal static async Task<MapSearchResult> FindMapAsync(
        BeatSaverSearchRequest request, CancellationToken cancellationToken)
    {
        var query = BuildQuery(request);
        // Discover the page count, then sample other pages so cached Random
        // ordering cannot repeatedly confine the roulette to its first page.
        var first = await FetchAsync(0, query, cancellationToken);
        var pageCount = Math.Max(1, (int?)first["info"]?["pages"] ?? 1);
        var usedPages = new HashSet<int>();
        var candidates = new Dictionary<string, EvaluatedDifficulty>(StringComparer.OrdinalIgnoreCase);
        var budget = Math.Min(pageCount, MaximumSamplePages);
        for (var attempt = 0; attempt < budget; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int page;
            lock (Random)
            {
                do { page = Random.Next(pageCount); } while (!usedPages.Add(page));
            }
            var response = page == 0 ? first : await FetchAsync(page, query, cancellationToken);
            foreach (var document in response["docs"]?.OfType<JObject>() ?? Enumerable.Empty<JObject>())
            {
                var candidate = EvaluateMap(document, request);
                if (candidate != null && !RecommendationEngine.IsInSessionHistory(candidate.Map))
                {
                    candidates[candidate.Map.Hash!] = candidate;
                }
            }
            // Two sampled pages give some variety without downloading an index.
            if (attempt >= 1 && candidates.Count > 0) break;
        }
        cancellationToken.ThrowIfCancellationRequested();
        Plugin.Log.Info($"[BeatSaver] Sampled {usedPages.Count} pages of {pageCount}; " +
                        $"{candidates.Count} eligible maps, NPS={BeatSaverSearchRequest.NpsLabels[request.NpsSelection]}, " +
                        $"NJS={BeatSaverSearchRequest.NjsLabels[request.NjsSelection]}.");
        return RecommendationEngine.SelectFromCandidates(candidates.Values);
    }

    internal static string BuildQuery(BeatSaverSearchRequest request)
    {
        var parameters = new List<string> { "order=Random", "pageSize=" + PageSize };
        void Add(string key, float? value)
        {
            if (value.HasValue)
                parameters.Add(key + "=" + value.Value.ToString("0.###", CultureInfo.InvariantCulture));
        }
        Add("minNps", request.NpsRange.Minimum);
        Add("maxNps", request.NpsRange.Maximum);
        Add("minDuration", request.Duration.GetMinimumSeconds());
        Add("maxDuration", request.Duration.GetMaximumSeconds());
        if (request.MinimumRating > 0) Add("minRating", request.MinimumRating);
        if (request.MinimumVotes > 0) parameters.Add("minVotes=" + request.MinimumVotes);
        if (request.StyleTag != null) parameters.Add("tags=" + Uri.EscapeDataString(request.StyleTag));
        if (request.StandardOnly) parameters.Add("characteristics=Standard");
        if (request.CuratedOnly) parameters.Add("curated=true");
        // Search has unusual automapper semantics: omitted = human maps,
        // false = AI only. Keep ranked and unranked maps in the same pool.
        return string.Join("&", parameters);
    }

    private static async Task<JObject> FetchAsync(int page, string query, CancellationToken token)
    {
        for (var attempt = 0; ; attempt++)
        {
            using var response = await Client.GetAsync(Api + page + "?" + query, token);
            if (((int)response.StatusCode == 429 || (int)response.StatusCode == 503) && attempt < 2)
            {
                var wait = response.Headers.RetryAfter?.Delta ?? TimeSpan.FromSeconds(attempt + 1);
                await Task.Delay(TimeSpan.FromSeconds(Math.Max(1, Math.Min(5, wait.TotalSeconds))), token);
                continue;
            }
            response.EnsureSuccessStatusCode();
            var json = await response.Content.ReadAsStringAsync();
            token.ThrowIfCancellationRequested();
            var result = JObject.Parse(json);
            if (!(result["docs"] is JArray))
                throw new HttpRequestException("BeatSaver returned an invalid search response.");
            return result;
        }
    }

    // Evaluate the exact published hash and exact difficulty, rather than
    // accepting every difficulty of a map that passed the search index filter.
    internal static EvaluatedDifficulty? EvaluateMap(JObject document, BeatSaverSearchRequest request)
    {
        if ((bool?)document["automapper"] == true || document["deletedAt"]?.Type == JTokenType.String)
            return null;
        var rating = (float?)document["stats"]?["score"] ?? 0f;
        var votes = ((int?)document["stats"]?["upvotes"] ?? 0) + ((int?)document["stats"]?["downvotes"] ?? 0);
        if (!Finite(rating) || rating < request.MinimumRating || votes < request.MinimumVotes) return null;
        if (request.CuratedOnly && string.IsNullOrWhiteSpace((string?)document["curatedAt"])) return null;
        if (request.StyleTag != null &&
            !(document["tags"]?.Values<string>().Contains(request.StyleTag) ?? false)) return null;
        var duration = (int?)document["metadata"]?["duration"];
        if (!request.Duration.Matches(duration)) return null;
        var version = document["versions"]?.OfType<JObject>()
            .Where(value => (string?)value["state"] == "Published")
            .OrderByDescending(value => (DateTimeOffset?)value["createdAt"] ?? DateTimeOffset.MinValue)
            .FirstOrDefault();
        var hash = (string?)version?["hash"];
        var url = (string?)version?["downloadURL"];
        if (string.IsNullOrWhiteSpace(hash) || string.IsNullOrWhiteSpace(url)) return null;
        var map = new RecommendationMap
        {
            Id = (string?)document["id"], Hash = hash,
            Name = (string?)document["metadata"]?["songName"] ?? (string?)document["name"],
            SubName = (string?)document["metadata"]?["songSubName"],
            Author = (string?)document["metadata"]?["songAuthorName"],
            Mapper = (string?)document["metadata"]?["levelAuthorName"] ?? (string?)document["uploader"]?["name"],
            Duration = duration, DownloadUrl = url,
            CoverImage = (string?)version?["coverURL"], FullCoverImage = (string?)version?["coverURL"],
            Difficulties = new List<RecommendationDifficulty>()
        };
        EvaluatedDifficulty? best = null;
        foreach (var diff in version?["diffs"]?.OfType<JObject>() ?? Enumerable.Empty<JObject>())
        {
            var nps = (float?)diff["nps"];
            var njs = (float?)diff["njs"];
            var mode = (string?)diff["characteristic"];
            var difficulty = (string?)diff["difficulty"];
            if (!nps.HasValue || !njs.HasValue || nps <= 0 || njs <= 0 ||
                !Matches(nps.Value, request.NpsRange) || !Matches(njs.Value, request.NjsRange) ||
                mode == null || !PlayableModes.Contains(mode) || request.StandardOnly && mode != "Standard" ||
                difficulty == null || !new[] { "Easy", "Normal", "Hard", "Expert", "ExpertPlus" }.Contains(difficulty)) continue;
            // The experimental pool consists of maps playable without extra
            // mapping/lighting/video plugins. No prerequisite dialogs mid-flow.
            if (new[] { "me", "ne", "vivify", "chroma", "cinema" }.Any(flag => (bool?)diff[flag] == true)) continue;
            var recommendation = new RecommendationDifficulty
            {
                DifficultyName = difficulty, ModeName = mode, Nps = nps, Njs = njs
            };
            map.Difficulties.Add(recommendation);
            var score = 0.55f * Proximity(nps.Value, request.NpsRange) +
                        0.15f * Proximity(njs.Value, request.NjsRange) +
                        0.30f * Math.Max(0f, Math.Min(1f, rating));
            if (best == null || score > best.Score) best = new EvaluatedDifficulty(map, recommendation, score);
        }
        return best;
    }

    private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    private static bool Matches(float value, (float? Minimum, float? Maximum) range) =>
        Finite(value) && (!range.Minimum.HasValue || value >= range.Minimum.Value) &&
        (!range.Maximum.HasValue || value <= range.Maximum.Value);

    private static float Proximity(float value, (float? Minimum, float? Maximum) range)
    {
        if (!range.Minimum.HasValue || !range.Maximum.HasValue) return 1f;
        var halfWidth = (range.Maximum.Value - range.Minimum.Value) / 2f;
        return 0.3f + 0.7f * Math.Max(0f, 1f - Math.Abs(value - range.Minimum.Value - halfWidth) / halfWidth);
    }
}
