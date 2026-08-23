using BeatLocator.EvaluationManagers;
using BeatLocator.WebUtils;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace BeatLocator.PostLevel;

internal sealed class PostLevelPpResolver
{
    private static readonly TimeSpan UploadTimeout = TimeSpan.FromSeconds(130);
    private readonly IPlatformUserModel _platformUserModel;

    public PostLevelPpResolver(IPlatformUserModel platformUserModel)
    {
        _platformUserModel = platformUserModel;
    }

    internal Task<ProviderPpBaseline?> CaptureBaselineAsync(
        RankingProvider provider,
        EvaluatedDifficulty selection,
        CancellationToken cancellationToken)
    {
        return provider == RankingProvider.ScoreSaber
            ? SSWebUtil.CapturePpBaselineAsync(
                _platformUserModel,
                selection,
                cancellationToken)
            : BLWebUtil.CapturePpBaselineAsync(selection, cancellationToken);
    }

    internal async Task<ProviderPpResult> ResolveAsync(
        RoulettePlaySession session,
        LevelCompletionResults results)
    {
        try
        {
            var baseline = await session.BaselineTask.ConfigureAwait(false);
            if (session.Provider == RankingProvider.BeatLeader)
            {
                return await ResolveBeatLeaderAsync(session, results, baseline)
                    .ConfigureAwait(false);
            }

            return await SSWebUtil.ResolvePpAsync(
                    session.Selection,
                    baseline,
                    session.LaunchedAt,
                    results.multipliedScore,
                    session.CancellationSource.Token)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return new ProviderPpResult
            {
                Outcome = PpResolutionOutcome.TimedOut,
                Detail = "PP request was canceled or timed out."
            };
        }
        catch (Exception exception)
        {
            return new ProviderPpResult
            {
                Outcome = PpResolutionOutcome.UploadFailed,
                Detail = exception.Message
            };
        }
    }

    private static async Task<ProviderPpResult> ResolveBeatLeaderAsync(
        RoulettePlaySession session,
        LevelCompletionResults results,
        ProviderPpBaseline? baseline)
    {
        if (session.BeatLeaderUploadTask == null)
        {
            return new ProviderPpResult
            {
                Outcome = PpResolutionOutcome.UploadFailed,
                Detail = "BeatLeader upload observer is unavailable."
            };
        }

        var timeoutTask = Task.Delay(UploadTimeout, session.CancellationSource.Token);
        var completedTask = await Task.WhenAny(session.BeatLeaderUploadTask, timeoutTask)
            .ConfigureAwait(false);
        if (completedTask != session.BeatLeaderUploadTask)
        {
            return new ProviderPpResult
            {
                Outcome = PpResolutionOutcome.TimedOut,
                Detail = "BeatLeader did not finish score upload within 130 seconds."
            };
        }

        var upload = await session.BeatLeaderUploadTask.ConfigureAwait(false);
        if (string.Equals(upload.Status, "NonPB", StringComparison.OrdinalIgnoreCase))
        {
            return new ProviderPpResult
            {
                Outcome = PpResolutionOutcome.NonPersonalBest,
                Detail = upload.Detail
            };
        }

        if (string.Equals(upload.Status, "Attempt", StringComparison.OrdinalIgnoreCase))
        {
            return new ProviderPpResult
            {
                Outcome = PpResolutionOutcome.Unranked,
                Detail = upload.Detail
            };
        }

        var isLegacyUpload = string.Equals(
            upload.Status,
            "UploadedLegacy",
            StringComparison.OrdinalIgnoreCase);
        if (!string.Equals(upload.State, "Finished", StringComparison.OrdinalIgnoreCase) ||
            !isLegacyUpload &&
            !string.Equals(upload.Status, "Uploaded", StringComparison.OrdinalIgnoreCase))
        {
            return new ProviderPpResult
            {
                Outcome = PpResolutionOutcome.UploadFailed,
                Detail = string.IsNullOrWhiteSpace(upload.Detail)
                    ? $"BeatLeader upload ended with {upload.State}/{upload.Status}."
                    : upload.Detail
            };
        }

        return await BLWebUtil.ResolveUploadedPpAsync(
                session.Selection,
                baseline,
                session.LaunchedAt,
                results.multipliedScore,
                isLegacyUpload,
                session.CancellationSource.Token)
            .ConfigureAwait(false);
    }
}
