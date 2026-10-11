using System;

namespace BeatLocator.EvaluationManagers;

internal static class BeatLeaderCandidateRules
{
    // The maps endpoint returns all difficulties of a matching song. Numeric
    // ratings (including speed ratings) do not imply that a difficulty is ranked.
    internal static bool IsRanked(RecommendationDifficulty difficulty) =>
        difficulty.BeatLeaderStatus == 3 && difficulty.Stars.HasValue &&
        difficulty.Stars.Value > 0 && !float.IsNaN(difficulty.Stars.Value) &&
        !float.IsInfinity(difficulty.Stars.Value);

    internal static bool MatchesMode(RecommendationDifficulty difficulty, bool onlyTwoSaber) =>
        !onlyTwoSaber || string.Equals(difficulty.ModeName, "Standard", StringComparison.OrdinalIgnoreCase);
}
