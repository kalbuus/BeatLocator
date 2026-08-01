using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Net.Http;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using IPA.Loader;
using IPA.Utilities.Async;
using Newtonsoft.Json;

namespace BeatLocator.WebUtils;

public static class BLWebUtil
{
    private const int AuthenticatedRequestTimeoutMilliseconds = 40000;
    private const float MinimumRankedStars = 1f;
    private const float MaximumRankedStars = 15f;
    private static readonly HttpClient HttpClient = new HttpClient();
    
    public static string? userId = string.Empty;
    
    /// <summary>
    /// Waits for BeatLeader to finish its normal sign-in/profile request and then loads
    /// scores for the signed-in BeatLeader account.
    /// </summary>
    public static async Task<List<ScoreEntry>?> FetchCurrentUserScoresAsync(
        int playNumber,
        CancellationToken cancellationToken = default)
    {
        userId = await GetBeatLeaderUserIdAsync(cancellationToken).ConfigureAwait(false);
        if (userId != null)
        {
            return await FetchScoresAsync(userId, playNumber, cancellationToken)
                .ConfigureAwait(false);
        }
        Plugin.Log.Warn("BeatLeader user ID was not available; scores were not requested.");
        return null;
    }

    private static async Task<string?> GetBeatLeaderUserIdAsync(
        CancellationToken cancellationToken)
    {
        // ProfileManager receives /user/modinterface after BeatLeader's login succeeds.
        // Its public TryGetUserId method avoids reading cookies or duplicating the auth flow.
        const string pluginId = "BeatLeader";
        const string profileManagerTypeName = "BeatLeader.DataManager.ProfileManager";
        const string tryGetUserIdMethodName = "TryGetUserId";

        for (var attempt = 0; attempt < 120; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var beatLeader = PluginManager.GetPluginFromId(pluginId);
            var profileManager = beatLeader?.Assembly.GetType(profileManagerTypeName);
            var tryGetUserId = profileManager?.GetMethod(
                tryGetUserIdMethodName,
                BindingFlags.Public | BindingFlags.Static);

            if (tryGetUserId != null)
            {
                object?[] arguments = { null };
                var hasProfile = tryGetUserId.Invoke(null, arguments) is true;
                if (hasProfile && arguments[0] is string tmpUserId && !string.IsNullOrWhiteSpace(tmpUserId))
                {
                    return tmpUserId;
                }
            }

            await Task.Delay(250, cancellationToken).ConfigureAwait(false);
        }

        return null;
    }

    private static async Task<List<ScoreEntry>?> FetchScoresAsync(
        string userId,
        int playNumber,
        CancellationToken cancellationToken)
    {
        try
        {
            var uri =
                GetBeatLeaderApiUrl() + $"/player/{Uri.EscapeDataString(userId)}/scores?sortBy=date&order=desc" +
                $"&page=1&count={playNumber}";
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            request.Headers.TryAddWithoutValidation("Accept", "text/plain");

            using var response = await HttpClient.SendAsync(request, cancellationToken)
                .ConfigureAwait(false);
            var responseBody = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();

            if (!response.IsSuccessStatusCode)
            {
                Plugin.Log.Warn($"BeatLeader returned {(int)response.StatusCode} ({response.ReasonPhrase}): {responseBody}");
                return null;
            }

            var result = JsonConvert.DeserializeObject<ScoresResponse>(responseBody);

            if (result != null && result.Data != null && result.Data.Count != 0) return result.Data;
            
            Plugin.Log.Error($"Error parsing the BL's response: {responseBody}");
            return null;

        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            Plugin.Log.Error($"Could not request BeatLeader scores: {exception}");
            return null;
        }
    }

    internal static async Task<List<MapEntry>?> FindMapAsync(
        float expectedStars,
        bool played,
        float starBuffer,
        bool onlyTwoSaber,
        int mapBalance,
        int mapDifficulty,
        int count,
        PluginConfig config,
        CancellationToken cancellationToken = default)
    {
        try
        {
            string sortIndex = mapDifficulty switch
            {
                0 or 4 => "stars",
                1 or 2 or 3 => "playCount",
                _ => "none"
            };
            string sortOrder = mapDifficulty switch
            {
                0 => "asc",
                1 or 2 or 3 or 4 => "desc",
                _ => "desc"
            };
            float minStars = Math.Max(MinimumRankedStars, expectedStars - starBuffer);
            float maxStars = Math.Min(MaximumRankedStars, expectedStars + starBuffer);
            
            string? modeSetting = onlyTwoSaber ? "mode=Standard" : null;
            string playedSetting = played ? "played" : "unplayed";
            var query = new List<string>
            {
                "page=1",
                $"count={count}",
                $"sortBy={sortIndex}",
                $"order={sortOrder}",
                "type=ranked",
                "allTypes=all",
                $"mytype={playedSetting}",
                $"stars_from={minStars.ToString(CultureInfo.InvariantCulture)}",
                $"stars_to={maxStars.ToString(CultureInfo.InvariantCulture)}",
                
            };
            if (modeSetting != null) query.Add(modeSetting);
            if (config.RecommendationsDurationEnabled)
            {
                query.Add($"duration_from={Math.Max(config.MinimumRecommendedSongDurationSeconds - 60, 0)}");
                query.Add($"duration_to={Math.Min(config.MaximumRecommendedSongDurationSeconds + 60, 600)}");
            }

            var uri = GetBeatLeaderApiUrl() + "/maps?" + string.Join("&", query);
            
            // This goes through BeatLeader.NetworkingUtils, which calls its own
            // Authentication.EnsureLoggedIn before sending the UnityWebRequest.
            // BeatLocator does not read platform tickets or session cookies.
            var responseBody = await SendAuthenticatedBeatLeaderRequestAsync(uri, cancellationToken)
                .ConfigureAwait(false);
            var result = JsonConvert.DeserializeObject<MapsResponse>(responseBody);

            var maps = result?.Data ?? new List<MapEntry>();
            foreach (var map in maps)
            {
                if (map.Difficulties == null) continue;

                map.Difficulties.RemoveAll(difficulty =>
                    !difficulty.Stars.HasValue ||
                    difficulty.Stars.Value < minStars ||
                    difficulty.Stars.Value > maxStars ||
                    onlyTwoSaber &&
                    (difficulty.ModeName == null ||
                     difficulty.ModeName.IndexOf("Standard", StringComparison.OrdinalIgnoreCase) < 0));
            }

            maps.RemoveAll(map => map.Difficulties == null || map.Difficulties.Count == 0);

            return maps;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            Plugin.Log.Error($"Could not find BeatLeader map: {exception}");
            return null;
        }
    }

    private static string GetBeatLeaderApiUrl()
    {
        const string constantsTypeName = "BeatLeader.Utils.BLConstants";
        const string apiUrlPropertyName = "BEATLEADER_API_URL";

        var beatLeader = PluginManager.GetPluginFromId("BeatLeader")
            ?? throw new InvalidOperationException("BeatLeader is not installed.");
        var constantsType = beatLeader.Assembly.GetType(constantsTypeName)
            ?? throw new MissingMemberException(constantsTypeName);
        var apiUrlProperty = constantsType.GetProperty(apiUrlPropertyName, BindingFlags.Public | BindingFlags.Static)
            ?? throw new MissingMemberException(constantsTypeName, apiUrlPropertyName);

        return apiUrlProperty.GetValue(null) as string
            ?? throw new InvalidOperationException("BeatLeader did not provide an API URL.");
    }

    private static async Task<string> SendAuthenticatedBeatLeaderRequestAsync(
        string uri,
        CancellationToken cancellationToken)
    {
        const string networkingUtilsTypeName = "BeatLeader.API.NetworkingUtils";
        const string rawGetDescriptorTypeName = "BeatLeader.API.RequestDescriptors.RawGetRequestDescriptor";
        const string simpleRequestMethodName = "SimpleRequestCoroutine";

        var beatLeader = PluginManager.GetPluginFromId("BeatLeader")
            ?? throw new InvalidOperationException("BeatLeader is not installed.");
        var networkingUtilsType = beatLeader.Assembly.GetType(networkingUtilsTypeName)
            ?? throw new MissingMemberException(networkingUtilsTypeName);
        var rawGetDescriptorType = beatLeader.Assembly.GetType(rawGetDescriptorTypeName)
            ?? throw new MissingMemberException(rawGetDescriptorTypeName);
        var descriptor = Activator.CreateInstance(rawGetDescriptorType, uri)
            ?? throw new InvalidOperationException("Could not create BeatLeader's request descriptor.");
        var simpleRequestMethod = Array.Find(
            networkingUtilsType.GetMethods(BindingFlags.Public | BindingFlags.Static),
            method => method.Name == simpleRequestMethodName && method.IsGenericMethodDefinition)
            ?? throw new MissingMethodException(networkingUtilsTypeName, simpleRequestMethodName);

        var completionSource = new TaskCompletionSource<byte[]>();
        Action<byte[]> onSuccess = bytes => completionSource.TrySetResult(bytes);
        Action<string> onFail = reason => completionSource.TrySetException(
            new InvalidOperationException($"BeatLeader authorization/request failed: {reason}"));
        var coroutine = simpleRequestMethod.MakeGenericMethod(typeof(byte[])).Invoke(
            null,
            new object?[] { descriptor, onSuccess, onFail, 1, 30 }) as IEnumerator
            ?? throw new InvalidOperationException("BeatLeader returned an unexpected coroutine.");

        using var timeoutSource =
            new CancellationTokenSource(AuthenticatedRequestTimeoutMilliseconds);
        using var linkedSource = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            timeoutSource.Token);
        using var registration = linkedSource.Token.Register(() =>
        {
            completionSource.TrySetCanceled();

            try
            {
                (coroutine as IDisposable)?.Dispose();
            }
            catch (Exception exception)
            {
                Plugin.Log.Warn(
                    $"Could not dispose the canceled BeatLeader request coroutine: {exception}");
            }
        });

        try
        {
            var responseBytes = await AwaitAuthenticatedResponseAsync(
                    coroutine,
                    completionSource.Task)
                .ConfigureAwait(false);
            return System.Text.Encoding.UTF8.GetString(responseBytes);
        }
        catch (OperationCanceledException) when (
            timeoutSource.IsCancellationRequested &&
            !cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException(
                "BeatLeader did not complete the authenticated request within 40 seconds.");
        }
    }

    private static async Task<byte[]> AwaitAuthenticatedResponseAsync(
        IEnumerator coroutine,
        Task<byte[]> responseTask)
    {
        var coroutineTask = Coroutines.AsTask(coroutine);
        var completedTask = await Task.WhenAny(coroutineTask, responseTask)
            .ConfigureAwait(false);

        if (completedTask == responseTask)
        {
            ObserveLateCoroutineFailure(coroutineTask);
            return await responseTask.ConfigureAwait(false);
        }

        await coroutineTask.ConfigureAwait(false);
        return await responseTask.ConfigureAwait(false);
    }

    private static void ObserveLateCoroutineFailure(Task coroutineTask)
    {
        coroutineTask.ContinueWith(
            task =>
            {
                _ = task.Exception;
            },
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted,
            TaskScheduler.Default);
    }

    #region SCORE JSON

    public sealed class ScoresResponse
    {
        [JsonProperty("data")]
        public List<ScoreEntry>? Data { get; set; }
    }
    
    public sealed class ScoreEntry
    {
        [JsonProperty("accuracy")]
        public float Accuracy { get; set; }
        
        [JsonProperty("timepost")]
        public int Timepost { get; set; } // Unix timestamp to know when the level was played

        [JsonProperty("leaderboard")]
        public Leaderboard? Leaderboard { get; set; }
    }
    
    public sealed class Leaderboard
    {
        [JsonProperty("difficulty")]
        public DifficultyScoreEntry? Difficulty { get; set; }
    }

    public sealed class DifficultyScoreEntry
    {
        [JsonProperty("stars")]
        public float? Stars { get; set; }
    }

    #endregion
    
    #region MAPS JSON
    
    public sealed class MapsResponse
    {
        [JsonProperty("data")]
        public List<MapEntry>? Data { get; set; }
    }

    public sealed class MapEntry
    {
        [JsonProperty("id")]
        public string? Id { get; set; }

        [JsonProperty("hash")]
        public string? Hash { get; set; }
        
        [JsonProperty("name")]
        public string? Name { get; set; }
        
        [JsonProperty("subName")]
        public string? SubName { get; set; }

        [JsonProperty("author")]
        public string? Author { get; set; }
        
        [JsonProperty("mapper")]
        public string? Mapper { get; set; }
        
        [JsonProperty("coverImage")]
        public string? CoverImage { get; set; }

        [JsonProperty("fullCoverImage")]
        public string? FullCoverImage { get; set; }
        
        [JsonProperty("downloadUrl")]
        public string? DownloadUrl { get; set; }
        
        [JsonProperty("duration")]
        public int? duration { get; set; } // duration in seconds
        
        [JsonProperty("difficulties")]
        public List<DifficultyEntry>? Difficulties { get; set; }
    }

    public sealed class DifficultyEntry
    {
        [JsonProperty("difficultyName")]
        public string? DifficultyName { get; set; }
        
        [JsonProperty("modeName")] // For only two saber mode can be either "Standard" or include the word "Standard"
        public string? ModeName { get; set; }
        
        [JsonProperty("stars")]
        public float? Stars {get; set;}
        
        [JsonProperty("passRating")]
        public float? PassRating { get; set; }
        
        [JsonProperty("techRating")]
        public float? TechRating { get; set; }
    }
    
    #endregion
    
}
