namespace BeatLocator.EvaluationManagers;

internal readonly struct MapSearchResult
{
    private MapSearchResult(EvaluatedDifficulty? selectedDifficulty, string? failureReason)
    {
        SelectedDifficulty = selectedDifficulty;
        FailureReason = failureReason;
    }

    internal EvaluatedDifficulty? SelectedDifficulty { get; }
    internal string? FailureReason { get; }
    internal bool IsSuccess => SelectedDifficulty != null;

    internal static MapSearchResult Success(EvaluatedDifficulty selectedDifficulty)
    {
        return new MapSearchResult(selectedDifficulty, null);
    }

    internal static MapSearchResult Failure(string reason)
    {
        return new MapSearchResult(null, reason);
    }
}
