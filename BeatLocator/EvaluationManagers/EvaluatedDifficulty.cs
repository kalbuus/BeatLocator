using BeatLocator.WebUtils;

namespace BeatLocator.EvaluationManagers;

/// <summary>
/// A single map difficulty that can participate in the final map selection.
/// </summary>
public sealed class EvaluatedDifficulty
{
    public EvaluatedDifficulty(
        BLWebUtil.MapEntry map,
        BLWebUtil.DifficultyEntry difficulty,
        float score)
    {
        Map = map;
        Difficulty = difficulty;
        Score = score;
    }

    public BLWebUtil.MapEntry Map { get; }

    public BLWebUtil.DifficultyEntry Difficulty { get; }

    public float Score { get; }
}
