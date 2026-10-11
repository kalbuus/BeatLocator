namespace BeatLocator.EvaluationManagers;

internal enum RankingProvider
{
    BeatLeader,
    ScoreSaber,
    BeatSaver
}

internal static class RankingProviderExtensions
{
    internal static string GetDisplayName(this RankingProvider provider)
    {
        return provider switch
        {
            RankingProvider.ScoreSaber => "ScoreSaber",
            RankingProvider.BeatSaver => "BeatSaver",
            _ => "BeatLeader"
        };
    }
}
