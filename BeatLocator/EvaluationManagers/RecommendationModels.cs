using System.Collections.Generic;
using Newtonsoft.Json;

namespace BeatLocator.EvaluationManagers;

/// <summary>
/// Provider-neutral data returned by BeatLeader and ScoreSaber adapters.
/// </summary>
internal sealed class PlayerScoreEntry
{
    [JsonProperty("accuracy")]
    public float Accuracy { get; set; }

    [JsonProperty("timepost")]
    public int Timepost { get; set; }

    [JsonProperty("leaderboard")]
    public ScoreLeaderboard? Leaderboard { get; set; }
}

internal sealed class ScoreLeaderboard
{
    [JsonProperty("difficulty")]
    public ScoreDifficulty? Difficulty { get; set; }
}

internal sealed class ScoreDifficulty
{
    [JsonProperty("stars")]
    public float? Stars { get; set; }
}

internal sealed class RecommendationMap
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
    public int? Duration { get; set; }

    [JsonProperty("difficulties")]
    public List<RecommendationDifficulty>? Difficulties { get; set; }
}

internal sealed class RecommendationDifficulty
{
    [JsonProperty("difficultyName")]
    public string? DifficultyName { get; set; }

    [JsonProperty("modeName")]
    public string? ModeName { get; set; }

    [JsonProperty("stars")]
    public float? Stars { get; set; }

    [JsonProperty("passRating")]
    public float? PassRating { get; set; }

    [JsonProperty("techRating")]
    public float? TechRating { get; set; }

    [JsonIgnore]
    public int? ProviderDifficultyId { get; set; }

    [JsonIgnore]
    public int? RealmId { get; set; }
}
