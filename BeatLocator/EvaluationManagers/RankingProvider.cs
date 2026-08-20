namespace BeatLocator.EvaluationManagers;

internal enum RankingProvider
{
    BeatLeader,
    ScoreSaber
}

internal static class RankingProviderExtensions
{
    internal static string GetDisplayName(this RankingProvider provider)
    {
        return provider == RankingProvider.ScoreSaber
            ? "ScoreSaber"
            : "BeatLeader";
    }
}
