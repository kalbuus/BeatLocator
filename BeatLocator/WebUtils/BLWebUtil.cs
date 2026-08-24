using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Net.Http;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using BeatLocator.EvaluationManagers;
using BeatLocator.Settings;
using IPA.Loader;
using IPA.Utilities.Async;
using Newtonsoft.Json;
using BeatLocator.PostLevel;

namespace BeatLocator.WebUtils;

internal static class BLWebUtil
{
    private const int AuthenticatedRequestTimeoutMilliseconds = 40000;
    private const int PpSettlementTimeoutSeconds = 60;
    private const int ZeroScorePpConfirmationSeconds = 2;
    private const int ZeroPpSettlementWindowSeconds = 30;
    private const int ProfileFallbackGraceSeconds = 10;
    private const float MinimumRankedStars = 1f;
    private const float MaximumRankedStars = 15f;
    private static readonly HttpClient HttpClient = new HttpClient();
    
    internal static string? userId = string.Empty;
    
    /// <summary>
    /// Waits for BeatLeader to finish its normal sign-in/profile request and then loads
    /// scores for the signed-in BeatLeader account.
    /// </summary>
    internal static async Task<List<PlayerScoreEntry>?> FetchCurrentUserScoresAsync(
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

    internal static async Task<string?> GetBeatLeaderUserIdAsync(
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

    private static async Task<List<PlayerScoreEntry>?> FetchScoresAsync(
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

    internal static async Task<List<RecommendationMap>?> FindMapAsync(
        float expectedStars,
        bool played,
        float starBuffer,
        bool onlyTwoSaber,
        int mapBalance,
        int mapDifficulty,
        SongDurationFilter durationFilter,
        int count,
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
            var minimumDuration = durationFilter.GetMinimumSeconds();
            var maximumDuration = durationFilter.GetMaximumSeconds();
            if (minimumDuration.HasValue)
                query.Add($"duration_from={minimumDuration.Value}");
            if (maximumDuration.HasValue)
                query.Add($"duration_to={maximumDuration.Value}");

            var uri = GetBeatLeaderApiUrl() + "/maps?" + string.Join("&", query);
            
            // This goes through BeatLeader.NetworkingUtils, which calls its own
            // Authentication.EnsureLoggedIn before sending the UnityWebRequest.
            // BeatLocator does not read platform tickets or session cookies.
            var responseBody = await SendAuthenticatedBeatLeaderRequestAsync(uri, cancellationToken)
                .ConfigureAwait(false);
            var result = JsonConvert.DeserializeObject<MapsResponse>(responseBody);

            var maps = result?.Data ?? new List<RecommendationMap>();
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

    internal static async Task<ProviderPpBaseline?> CapturePpBaselineAsync(
        EvaluatedDifficulty selection,
        CancellationToken cancellationToken)
    {
        var playerId = await GetBeatLeaderUserIdAsync(cancellationToken)
            .ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(playerId)) return null;

        try
        {
            var uri = GetBeatLeaderApiUrl() +
                      $"/player/{Uri.EscapeDataString(playerId)}";
            var profile = await GetPublicJsonAsync<BeatLeaderProfile>(uri, cancellationToken)
                .ConfigureAwait(false);
            var scoresUri = GetBeatLeaderApiUrl() +
                            $"/player/{Uri.EscapeDataString(playerId)}/scores" +
                            "?sortBy=date&order=desc&page=1&count=100";
            var scores = await GetPublicJsonAsync<BeatLeaderPpScoresResponse>(
                    scoresUri,
                    cancellationToken)
                .ConfigureAwait(false);
            var personalBest = scores.Data?.Find(score =>
                IsMatchingBeatLeaderLeaderboard(score, selection));
            Plugin.Log.Debug(
                $"[PP] BeatLeader baseline: player_pp={profile.Pp.ToString("0.000000", CultureInfo.InvariantCulture)}, " +
                $"pb_score_id={personalBest?.Id.ToString(CultureInfo.InvariantCulture) ?? "none"}.");
            return new ProviderPpBaseline
            {
                PlayerId = playerId,
                TotalPp = profile.Pp,
                PersonalBestScoreId = personalBest?.Id,
                PersonalBestModifiedScore = personalBest?.ModifiedScore,
                PersonalBestPp = personalBest?.Pp,
                CapturedAt = DateTimeOffset.UtcNow
            };
        }
        catch (Exception exception) when (!(exception is OperationCanceledException))
        {
            Plugin.Log.Warn($"[PP] Could not capture BeatLeader PP baseline: {exception.Message}");
            return new ProviderPpBaseline
            {
                PlayerId = playerId,
                CapturedAt = DateTimeOffset.UtcNow
            };
        }
    }

    internal static async Task<ProviderPpResult> ResolveUploadedPpAsync(
        EvaluatedDifficulty selection,
        ProviderPpBaseline? baseline,
        DateTimeOffset launchedAt,
        int modifiedScore,
        bool allowLegacyNonPbDetection,
        CancellationToken cancellationToken)
    {
        var playerId = baseline?.PlayerId ??
                       await GetBeatLeaderUserIdAsync(cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(playerId))
        {
            return new ProviderPpResult
            {
                Outcome = PpResolutionOutcome.UploadFailed,
                Detail = "BeatLeader player ID is unavailable."
            };
        }

        var deadline = DateTimeOffset.UtcNow.AddSeconds(PpSettlementTimeoutSeconds);
        var nonPbCheckAfter = DateTimeOffset.UtcNow.AddSeconds(20);
        BeatLeaderPpScore? latestScore = null;
        DateTimeOffset? scoreFirstSeenAt = null;
        DateTimeOffset? profileGainFirstSeenAt = null;
        double? measuredProfileGain = null;
        while (DateTimeOffset.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var uri = GetBeatLeaderApiUrl() +
                      $"/player/{Uri.EscapeDataString(playerId)}/scores" +
                      "?sortBy=date&order=desc&page=1&count=20&includeIO=true";
            var response = await GetPublicJsonAsync<BeatLeaderPpScoresResponse>(
                    uri,
                    cancellationToken)
                .ConfigureAwait(false);
            var leaderboardScore = response.Data?.Find(candidate =>
                IsMatchingBeatLeaderLeaderboard(candidate, selection));
            var score = response.Data?.Find(candidate =>
                IsMatchingBeatLeaderScore(
                    candidate,
                    selection,
                    launchedAt,
                    modifiedScore));
            if (score != null)
            {
                latestScore = score;
                if (!scoreFirstSeenAt.HasValue)
                {
                    scoreFirstSeenAt = DateTimeOffset.UtcNow;
                    Plugin.Log.Info(
                        $"[PP] BeatLeader score {score.Id} is visible; waiting for profile PP settlement.");
                }

                // A No Fail run can still be reported by Beat Saber as Cleared after the
                // energy bar was depleted. BeatLeader publishes that score, but assigns it
                // zero PP. In that case no profile settlement can arrive, so do not leave
                // the result screen waiting for the generic zero-gain timeout.
                if (score.Pp <= 0.000001d &&
                    DateTimeOffset.UtcNow - scoreFirstSeenAt.Value >=
                    TimeSpan.FromSeconds(ZeroScorePpConfirmationSeconds))
                {
                    return CreateBeatLeaderPpResult(
                        score,
                        0d,
                        "BeatLeader confirmed zero score PP; no profile settlement is expected");
                }

                var reportedProfileGain = score.ScoreImprovement?.TotalPp;
                if (reportedProfileGain.HasValue &&
                    Math.Abs(reportedProfileGain.Value) > 0.000001d)
                {
                    return CreateBeatLeaderPpResult(
                        score,
                        reportedProfileGain,
                        "profile gain was reported by scoreImprovement.totalPp");
                }

                if (baseline?.TotalPp.HasValue == true)
                {
                    var profileUri = GetBeatLeaderApiUrl() +
                                     $"/player/{Uri.EscapeDataString(playerId)}";
                    var profile = await GetPublicJsonAsync<BeatLeaderProfile>(
                            profileUri,
                            cancellationToken)
                        .ConfigureAwait(false);
                    var currentMeasuredGain = profile.Pp - baseline.TotalPp.Value;
                    if (Math.Abs(currentMeasuredGain) > 0.000001d)
                    {
                        if (!measuredProfileGain.HasValue ||
                            Math.Abs(measuredProfileGain.Value - currentMeasuredGain) > 0.0005d)
                        {
                            measuredProfileGain = currentMeasuredGain;
                            profileGainFirstSeenAt = DateTimeOffset.UtcNow;
                        }

                        if (profileGainFirstSeenAt.HasValue &&
                            DateTimeOffset.UtcNow - profileGainFirstSeenAt.Value >=
                            TimeSpan.FromSeconds(ProfileFallbackGraceSeconds))
                        {
                            return CreateBeatLeaderPpResult(
                                score,
                                measuredProfileGain,
                                "profile gain was measured from stable before/after player.pp snapshots");
                        }
                    }
                }

                if (DateTimeOffset.UtcNow - scoreFirstSeenAt.Value >=
                    TimeSpan.FromSeconds(ZeroPpSettlementWindowSeconds))
                {
                    return CreateBeatLeaderPpResult(
                        score,
                        measuredProfileGain ?? reportedProfileGain ?? 0d,
                        "BeatLeader profile remained stable throughout the PP settlement window");
                }
            }

            if (allowLegacyNonPbDetection &&
                DateTimeOffset.UtcNow >= nonPbCheckAfter &&
                baseline?.PersonalBestScoreId.HasValue == true &&
                leaderboardScore?.Id == baseline.PersonalBestScoreId.Value)
            {
                return new ProviderPpResult
                {
                    Outcome = PpResolutionOutcome.NonPersonalBest,
                    Detail = $"BeatLeader kept pre-play score {leaderboardScore.Id}; " +
                             "the 0.9.x upload API does not expose a separate NonPB status."
                };
            }

            await Task.Delay(2500, cancellationToken).ConfigureAwait(false);
        }

        if (latestScore != null)
        {
            return CreateBeatLeaderPpResult(
                latestScore,
                measuredProfileGain ?? latestScore.ScoreImprovement?.TotalPp ?? 0d,
                "PP settlement reached the overall timeout; using the last stable provider value");
        }

        return new ProviderPpResult
        {
            Outcome = PpResolutionOutcome.TimedOut,
            Detail = "BeatLeader accepted the upload, but the matching score did not appear in the public API."
        };
    }

    private static ProviderPpResult CreateBeatLeaderPpResult(
        BeatLeaderPpScore score,
        double? profileGain,
        string source)
    {
        return new ProviderPpResult
        {
            Outcome = PpResolutionOutcome.UploadedNewBest,
            ScorePp = score.Pp,
            ProfileGain = profileGain,
            Detail = $"score_id={score.Id}, raw_pp_improvement=" +
                     $"{score.ScoreImprovement?.Pp.ToString("0.00", CultureInfo.InvariantCulture) ?? "n/a"}; " +
                     source
        };
    }

    private static bool IsMatchingBeatLeaderScore(
        BeatLeaderPpScore score,
        EvaluatedDifficulty selection,
        DateTimeOffset launchedAt,
        int modifiedScore)
    {
        if (!IsMatchingBeatLeaderLeaderboard(score, selection) ||
            score.ModifiedScore != modifiedScore ||
            score.Timepost < launchedAt.AddSeconds(-10).ToUnixTimeSeconds())
        {
            return false;
        }

        return true;
    }

    private static bool IsMatchingBeatLeaderLeaderboard(
        BeatLeaderPpScore score,
        EvaluatedDifficulty selection)
    {
        var expectedHash = selection.Map.Hash;
        if (string.IsNullOrWhiteSpace(expectedHash) ||
            !string.Equals(
                score.Leaderboard?.Song?.Hash,
                expectedHash,
                StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var expectedDifficulty = NormalizeDifficulty(selection.Difficulty.DifficultyName);
        var actualDifficulty = NormalizeDifficulty(score.Leaderboard?.Difficulty?.DifficultyName);
        var expectedMode = NormalizeMode(selection.Difficulty.ModeName);
        var actualMode = NormalizeMode(score.Leaderboard?.Difficulty?.ModeName);
        return expectedDifficulty == actualDifficulty && expectedMode == actualMode;
    }

    private static string NormalizeDifficulty(string? value)
    {
        return (value ?? string.Empty).Replace("+", "plus")
            .Replace("_", string.Empty)
            .ToLowerInvariant();
    }

    private static string NormalizeMode(string? value)
    {
        var normalized = (value ?? string.Empty).Replace("_", string.Empty)
            .ToLowerInvariant();
        return normalized.Contains("standard") ? "standard" : normalized;
    }

    private static async Task<T> GetPublicJsonAsync<T>(
        string uri,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.TryAddWithoutValidation("Accept", "application/json");
        using var response = await HttpClient.SendAsync(request, cancellationToken)
            .ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException(
                $"BeatLeader returned {(int)response.StatusCode} ({response.ReasonPhrase}): {body}");
        }

        return JsonConvert.DeserializeObject<T>(body)
               ?? throw new JsonException("BeatLeader returned an empty or invalid response.");
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

    #region SCORE/PROFILE JSON

    private sealed class ScoresResponse
    {
        [JsonProperty("data")]
        public List<PlayerScoreEntry>? Data { get; set; }
    }

    private sealed class BeatLeaderProfile
    {
        [JsonProperty("pp")]
        public double Pp { get; set; }
    }

    private sealed class BeatLeaderPpScoresResponse
    {
        [JsonProperty("data")]
        public List<BeatLeaderPpScore>? Data { get; set; }
    }

    private sealed class BeatLeaderPpScore
    {
        [JsonProperty("id")]
        public long Id { get; set; }

        [JsonProperty("modifiedScore")]
        public int ModifiedScore { get; set; }

        [JsonProperty("pp")]
        public double Pp { get; set; }

        [JsonProperty("timepost")]
        public long Timepost { get; set; }

        [JsonProperty("leaderboard")]
        public BeatLeaderPpLeaderboard? Leaderboard { get; set; }

        [JsonProperty("scoreImprovement")]
        public BeatLeaderScoreImprovement? ScoreImprovement { get; set; }
    }

    private sealed class BeatLeaderPpLeaderboard
    {
        [JsonProperty("song")]
        public BeatLeaderPpSong? Song { get; set; }

        [JsonProperty("difficulty")]
        public BeatLeaderPpDifficulty? Difficulty { get; set; }
    }

    private sealed class BeatLeaderPpSong
    {
        [JsonProperty("hash")]
        public string? Hash { get; set; }
    }

    private sealed class BeatLeaderPpDifficulty
    {
        [JsonProperty("difficultyName")]
        public string? DifficultyName { get; set; }

        [JsonProperty("modeName")]
        public string? ModeName { get; set; }
    }

    private sealed class BeatLeaderScoreImprovement
    {
        [JsonProperty("pp")]
        public double Pp { get; set; }

        [JsonProperty("totalPp")]
        public double TotalPp { get; set; }
    }

    #endregion

    #region MAPS JSON

    private sealed class MapsResponse
    {
        [JsonProperty("data")]
        public List<RecommendationMap>? Data { get; set; }
    }
    
    #endregion
    
}
