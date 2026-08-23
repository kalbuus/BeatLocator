using BeatLocator.EvaluationManagers;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace BeatLocator.PostLevel;

internal enum PpResolutionOutcome
{
    UploadedNewBest,
    NonPersonalBest,
    UploadFailed,
    TimedOut,
    Invalidated,
    Unranked
}

internal sealed class ProviderPpBaseline
{
    internal string? PlayerId { get; set; }
    internal double? TotalPp { get; set; }
    internal int? PersonalBestModifiedScore { get; set; }
    internal double? PersonalBestPp { get; set; }
    internal long? PersonalBestScoreId { get; set; }
    internal DateTimeOffset CapturedAt { get; set; }
}

internal sealed class ProviderPpResult
{
    internal PpResolutionOutcome Outcome { get; set; }
    internal double? ScorePp { get; set; }
    internal double? ProfileGain { get; set; }
    internal string? Detail { get; set; }
}

internal sealed class RoulettePlaySession : IDisposable
{
    internal RoulettePlaySession(
        long runId,
        RankingProvider provider,
        EvaluatedDifficulty selection,
        BeatmapKey beatmapKey,
        Task<ProviderPpBaseline?> baselineTask,
        Task<BeatLeaderUploadResult>? beatLeaderUploadTask,
        CancellationTokenSource cancellationSource)
    {
        RunId = runId;
        Provider = provider;
        Selection = selection;
        BeatmapKey = beatmapKey;
        BaselineTask = baselineTask;
        BeatLeaderUploadTask = beatLeaderUploadTask;
        CancellationSource = cancellationSource;
        LaunchedAt = DateTimeOffset.UtcNow;
    }

    internal long RunId { get; }
    internal RankingProvider Provider { get; }
    internal EvaluatedDifficulty Selection { get; }
    internal BeatmapKey BeatmapKey { get; }
    internal DateTimeOffset LaunchedAt { get; }
    internal Task<ProviderPpBaseline?> BaselineTask { get; }
    internal Task<BeatLeaderUploadResult>? BeatLeaderUploadTask { get; }
    internal CancellationTokenSource CancellationSource { get; }
    internal bool CompletionClaimed { get; set; }

    public void Dispose()
    {
        CancellationSource.Dispose();
    }
}

internal sealed class BeatLeaderUploadResult
{
    internal string State { get; set; } = string.Empty;
    internal string Status { get; set; } = string.Empty;
    internal string Detail { get; set; } = string.Empty;
}
