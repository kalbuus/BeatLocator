namespace BeatLocator.EvaluationManagers;

internal sealed class EvaluatedDifficulty
{
    internal EvaluatedDifficulty(
        RecommendationMap map,
        RecommendationDifficulty difficulty,
        float score)
    {
        Map = map;
        Difficulty = difficulty;
        Score = score;
    }

    internal RecommendationMap Map { get; }

    internal RecommendationDifficulty Difficulty { get; }

    internal float Score { get; }
}
