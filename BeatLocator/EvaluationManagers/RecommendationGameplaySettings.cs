namespace BeatLocator.EvaluationManagers;

internal static class RecommendationGameplaySettings
{
    internal static GameplayModifiers.SongSpeed GetSongSpeed(PlayerDataModel? playerDataModel)
    {
        // The Solo panel's gameplayModifiers remains null until Setup/first activation.
        // Native modifier-change events already write the current value to playerData.
        return playerDataModel?.playerData?.gameplayModifiers?.songSpeed ?? GameplayModifiers.SongSpeed.Normal;
    }
}
