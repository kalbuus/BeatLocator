using BeatLocator.EvaluationManagers;
using System;
using System.Threading;

namespace BeatLocator.PostLevel;

internal sealed class RoulettePlaySessionManager
{
    private static readonly TimeSpan MaximumSessionAge = TimeSpan.FromMinutes(15);
    private readonly object _sync = new object();
    private readonly PostLevelPpResolver _ppResolver;
    private readonly BeatLeaderUploadObserver _beatLeaderUploadObserver;
    private readonly PostLevelUiState _uiState;
    private long _nextRunId;
    private RoulettePlaySession? _current;

    public RoulettePlaySessionManager(
        PostLevelPpResolver ppResolver,
        BeatLeaderUploadObserver beatLeaderUploadObserver,
        PostLevelUiState uiState)
    {
        _ppResolver = ppResolver;
        _beatLeaderUploadObserver = beatLeaderUploadObserver;
        _uiState = uiState;
    }

    internal long Begin(
        RankingProvider provider,
        EvaluatedDifficulty selection,
        BeatmapKey beatmapKey)
    {
        RoulettePlaySession? previous;
        RoulettePlaySession current;
        lock (_sync)
        {
            previous = _current;
            var runId = ++_nextRunId;
            var cancellationSource = new CancellationTokenSource();
            var uploadTask = provider == RankingProvider.BeatLeader
                ? _beatLeaderUploadObserver.Arm(runId)
                : null;
            current = new RoulettePlaySession(
                runId,
                provider,
                selection,
                beatmapKey,
                _ppResolver.CaptureBaselineAsync(
                    provider,
                    selection,
                    cancellationSource.Token),
                uploadTask,
                cancellationSource);
            _current = current;
        }

        _uiState.Begin(current.RunId, current.Provider);
        CancelAndDispose(previous);
        Plugin.Log.Info(
            $"[PP] Armed roulette run {current.RunId}: provider={provider.GetDisplayName()}, " +
            $"map={selection.Map.Hash ?? selection.Map.Id ?? "<unknown>"}, " +
            $"key={DescribeKey(beatmapKey)}.");
        return current.RunId;
    }

    internal RoulettePlaySession? TryClaim(BeatmapKey beatmapKey)
    {
        lock (_sync)
        {
            if (_current == null ||
                _current.CompletionClaimed ||
                DateTimeOffset.UtcNow - _current.LaunchedAt > MaximumSessionAge ||
                !KeysMatch(_current.BeatmapKey, beatmapKey))
            {
                return null;
            }

            _current.CompletionClaimed = true;
            return _current;
        }
    }

    internal bool MatchesActive(BeatmapKey beatmapKey)
    {
        lock (_sync)
        {
            return _current != null && KeysMatch(_current.BeatmapKey, beatmapKey);
        }
    }

    internal void RearmAfterRestart(RoulettePlaySession session)
    {
        Begin(session.Provider, session.Selection, session.BeatmapKey);
    }

    internal void Release(long runId)
    {
        RoulettePlaySession? released = null;
        lock (_sync)
        {
            if (_current?.RunId == runId)
            {
                released = _current;
                _current = null;
            }
        }

        CancelAndDispose(released);
    }

    internal void CancelCurrent()
    {
        RoulettePlaySession? canceled;
        lock (_sync)
        {
            canceled = _current;
            _current = null;
        }

        if (canceled != null)
        {
            _uiState.Abandon(canceled.RunId);
        }
        CancelAndDispose(canceled);
    }

    private void CancelAndDispose(RoulettePlaySession? session)
    {
        if (session == null) return;
        _beatLeaderUploadObserver.Disarm(session.RunId);
        session.CancellationSource.Cancel();
        session.Dispose();
    }

    private static bool KeysMatch(BeatmapKey left, BeatmapKey right)
    {
        return left.levelId == right.levelId &&
               left.difficulty == right.difficulty &&
               left.beatmapCharacteristic != null &&
               right.beatmapCharacteristic != null &&
               left.beatmapCharacteristic.serializedName ==
               right.beatmapCharacteristic.serializedName;
    }

    private static string DescribeKey(BeatmapKey key)
    {
        return $"{key.levelId}/{key.beatmapCharacteristic?.serializedName ?? "<none>"}/{key.difficulty}";
    }
}
