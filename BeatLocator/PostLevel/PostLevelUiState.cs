using BeatLocator.EvaluationManagers;
using System;

namespace BeatLocator.PostLevel;

internal sealed class PostLevelDisplayResult
{
    internal long RunId { get; set; }
    internal RankingProvider Provider { get; set; }
    internal PpResolutionOutcome Outcome { get; set; }
    internal double? ScorePp { get; set; }
    internal double? ProfileGain { get; set; }
    internal int LocalScore { get; set; }
    internal string LocalRank { get; set; } = string.Empty;
    internal int LocalMaxCombo { get; set; }
    internal string Detail { get; set; } = string.Empty;
}

internal sealed class PostLevelTerminalResult
{
    internal long RunId { get; set; }
    internal RankingProvider Provider { get; set; }
    internal EvaluatedDifficulty Selection { get; set; } = null!;
    internal bool LevelFailed { get; set; }
}

/// <summary>
/// Joins two independently timed signals: the player leaving vanilla results
/// and the ranking provider finishing PP settlement.
/// </summary>
internal sealed class PostLevelUiState
{
    private readonly object _sync = new object();
    private long _activeRunId;
    private RankingProvider _provider;
    // Arming a launch is not enough to own the next Results screen. The exact
    // BeatmapKey must first be claimed by RouletteLevelCompletionTracker.
    private bool _completionConfirmed;
    private bool _vanillaContinuePressed;
    private bool _taken;
    private PostLevelDisplayResult? _result;
    private PostLevelTerminalResult? _terminalResult;
    private bool _terminalTaken;

    internal static PostLevelUiState? Instance { get; private set; }

    public PostLevelUiState()
    {
        Instance = this;
    }

    internal event Action? ReadyChanged;

    internal bool ShouldInterceptVanillaContinue()
    {
        lock (_sync)
        {
            return _activeRunId != 0 &&
                   _completionConfirmed &&
                   _terminalResult == null &&
                   !_taken;
        }
    }

    internal bool HasFailedTerminalForActiveRun()
    {
        lock (_sync)
        {
            return _activeRunId != 0 &&
                   !_terminalTaken &&
                   _terminalResult?.LevelFailed == true;
        }
    }

    internal bool HasQuitTerminalForActiveRun()
    {
        lock (_sync)
        {
            return _activeRunId != 0 &&
                   _completionConfirmed &&
                   !_terminalTaken &&
                   _terminalResult?.LevelFailed == false;
        }
    }

    internal void Begin(long runId, RankingProvider provider)
    {
        lock (_sync)
        {
            _activeRunId = runId;
            _provider = provider;
            _completionConfirmed = false;
            _vanillaContinuePressed = false;
            _taken = false;
            _result = null;
            _terminalResult = null;
            _terminalTaken = false;
        }
    }

    internal bool ConfirmCompletion(RoulettePlaySession session)
    {
        lock (_sync)
        {
            if (_activeRunId != session.RunId || _taken)
            {
                return false;
            }

            _completionConfirmed = true;
            return true;
        }
    }

    internal bool MarkVanillaContinuePressed(
        out long runId,
        out RankingProvider provider)
    {
        var notify = false;
        lock (_sync)
        {
            if (_activeRunId == 0 ||
                !_completionConfirmed ||
                _terminalResult != null ||
                _taken)
            {
                runId = 0;
                provider = default;
                return false;
            }
            _vanillaContinuePressed = true;
            notify = _result != null;
            runId = _activeRunId;
            provider = _provider;
        }

        if (notify) ReadyChanged?.Invoke();
        return true;
    }

    internal void Complete(
        RoulettePlaySession session,
        ProviderPpResult providerResult,
        LevelCompletionResults localResults)
    {
        var notify = false;
        lock (_sync)
        {
            if (_activeRunId != session.RunId || !_completionConfirmed || _taken) return;
            _result = new PostLevelDisplayResult
            {
                RunId = session.RunId,
                Provider = session.Provider,
                Outcome = providerResult.Outcome,
                ScorePp = providerResult.ScorePp,
                ProfileGain = providerResult.ProfileGain,
                LocalScore = localResults.multipliedScore,
                LocalRank = localResults.rank.ToString(),
                LocalMaxCombo = localResults.maxCombo,
                Detail = providerResult.Detail ?? string.Empty
            };
            notify = _vanillaContinuePressed;
        }

        if (notify) ReadyChanged?.Invoke();
    }

    internal void CompleteTerminal(
        RoulettePlaySession session,
        bool levelFailed)
    {
        lock (_sync)
        {
            if (_activeRunId != session.RunId ||
                !_completionConfirmed ||
                _terminalTaken)
            {
                return;
            }
            _terminalResult = new PostLevelTerminalResult
            {
                RunId = session.RunId,
                Provider = session.Provider,
                Selection = session.Selection,
                LevelFailed = levelFailed
            };
        }

        ReadyChanged?.Invoke();
    }

    internal bool TryTakeReady(out PostLevelDisplayResult? result)
    {
        lock (_sync)
        {
            if (_activeRunId == 0 ||
                !_completionConfirmed ||
                !_vanillaContinuePressed ||
                _result == null ||
                _taken)
            {
                result = null;
                return false;
            }

            _taken = true;
            result = _result;
            return true;
        }
    }

    internal bool TryTakeTerminal(out PostLevelTerminalResult? result)
    {
        lock (_sync)
        {
            if (_activeRunId == 0 ||
                !_completionConfirmed ||
                _terminalResult == null ||
                _terminalTaken)
            {
                result = null;
                return false;
            }

            _terminalTaken = true;
            _taken = true;
            result = _terminalResult;
            return true;
        }
    }

    internal bool TryTakeQuitTerminal(out PostLevelTerminalResult? result)
    {
        lock (_sync)
        {
            if (_activeRunId == 0 ||
                !_completionConfirmed ||
                _terminalResult == null ||
                _terminalResult.LevelFailed ||
                _terminalTaken)
            {
                result = null;
                return false;
            }

            _terminalTaken = true;
            _taken = true;
            result = _terminalResult;
            return true;
        }
    }

    internal void Abandon(long runId)
    {
        lock (_sync)
        {
            if (_activeRunId != runId) return;
            _activeRunId = 0;
            _completionConfirmed = false;
            _result = null;
            _terminalResult = null;
            _taken = false;
            _terminalTaken = false;
            _vanillaContinuePressed = false;
        }
    }
}
