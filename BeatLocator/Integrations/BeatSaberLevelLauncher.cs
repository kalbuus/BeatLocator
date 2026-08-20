using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using BeatLocator.EvaluationManagers;

namespace BeatLocator.Integrations;

internal static class BeatSaberLevelLauncher
{
    private const string CustomLevelIdPrefix = "custom_level_";
    private const int LevelLoadTimeoutMilliseconds = 15000;
    private const int LevelLoadPollMilliseconds = 100;
    private const int SelectionTimeoutMilliseconds = 5000;
    private const int SelectionPollMilliseconds = 50;

    internal sealed class ResolvedLevel
    {
        public ResolvedLevel(BeatmapLevel level, BeatmapKey key)
        {
            Level = level;
            Key = key;
        }

        public BeatmapLevel Level { get; }
        public BeatmapKey Key { get; }
    }

    public static async Task<ResolvedLevel> ResolveAsync(
        EvaluatedDifficulty selectedDifficulty)
    {
        var hash = BetterSongSearchDownloadManager.GetMapHash(selectedDifficulty.Map);
        var levelId = CustomLevelIdPrefix + hash.ToUpperInvariant();
        var level = SongCore.Loader.BeatmapLevelsModelSO.GetBeatmapLevel(levelId);

        if (level == null)
        {
            SongCore.Loader.Instance.RefreshSongs(false);

            var attempts = LevelLoadTimeoutMilliseconds / LevelLoadPollMilliseconds;
            for (var attempt = 0; attempt < attempts && level == null; attempt++)
            {
                await Task.Delay(LevelLoadPollMilliseconds);
                level = SongCore.Loader.BeatmapLevelsModelSO.GetBeatmapLevel(levelId);
            }
        }

        if (level == null)
        {
            throw new InvalidOperationException(
                $"SongCore did not load map {hash} within 15 seconds.");
        }

        var key = FindBeatmapKey(level, selectedDifficulty.Difficulty);
        return new ResolvedLevel(level, key);
    }

    public static async Task SelectDifficultyAsync(
        SoloFreePlayFlowCoordinator flowCoordinator,
        ResolvedLevel resolvedLevel)
    {
        var navigationController = GetFieldValue(
            flowCoordinator,
            typeof(LevelSelectionFlowCoordinator),
            "levelSelectionNavigationController");
        var collectionController = GetFieldValue(
            navigationController,
            navigationController.GetType(),
            "_levelCollectionNavigationController");
        var detailController = GetFieldValue(
            collectionController,
            collectionController.GetType(),
            "_levelDetailViewController");
        var detailView = GetFieldValue(
            detailController,
            detailController.GetType(),
            "_standardLevelDetailView");

        var currentKeyProperty = detailView.GetType().GetProperty(
            "beatmapKey",
            BindingFlags.Public | BindingFlags.Instance)
            ?? throw new MissingMemberException(detailView.GetType().Name, "beatmapKey");

        var attempts = SelectionTimeoutMilliseconds / SelectionPollMilliseconds;
        BeatmapKey currentKey = default;
        for (var attempt = 0; attempt < attempts; attempt++)
        {
            currentKey = (BeatmapKey)currentKeyProperty.GetValue(detailView, null);
            if (currentKey.levelId == resolvedLevel.Key.levelId &&
                currentKey.beatmapCharacteristic != null)
            {
                break;
            }

            await Task.Delay(SelectionPollMilliseconds);
        }

        if (currentKey.levelId != resolvedLevel.Key.levelId ||
            currentKey.beatmapCharacteristic == null)
        {
            throw new InvalidOperationException(
                "Beat Saber's level detail screen did not finish loading within 5 seconds.");
        }

        var allowedDifficultyMask = GetFieldValue(
            detailController,
            detailController.GetType(),
            "_allowedBeatmapDifficultyMask");
        var notAllowedCharacteristics = GetFieldValue(
            detailController,
            detailController.GetType(),
            "_notAllowedCharacteristics");
        var playerData = GetFieldValue(
            detailView,
            detailView.GetType(),
            "_playerData");
        var setContentMethod = detailView.GetType().GetMethods(
                BindingFlags.Public | BindingFlags.Instance)
            .SingleOrDefault(method =>
                method.Name == "SetContent" && method.GetParameters().Length == 6)
            ?? throw new MissingMethodException(detailView.GetType().Name, "SetContent");

        setContentMethod.Invoke(detailView, new[]
        {
            (object)resolvedLevel.Level,
            allowedDifficultyMask,
            notAllowedCharacteristics,
            resolvedLevel.Key.difficulty,
            resolvedLevel.Key.beatmapCharacteristic,
            playerData
        });

        // Let the segmented controls apply their values before the normal Play
        // handler reads the selected key.
        await Task.Yield();

        currentKey = (BeatmapKey)currentKeyProperty.GetValue(detailView, null);
        if (!KeysMatch(currentKey, resolvedLevel.Key))
        {
            throw new InvalidOperationException(
                $"Beat Saber selected {DescribeKey(currentKey)} instead of " +
                $"{DescribeKey(resolvedLevel.Key)}.");
        }

        Plugin.Log.Info($"Selected Beat Saber difficulty {DescribeKey(currentKey)}.");
    }

    private static object GetFieldValue(object instance, Type declaringType, string fieldName)
    {
        var field = declaringType.GetField(
            fieldName,
            BindingFlags.NonPublic | BindingFlags.Instance)
            ?? throw new MissingFieldException(declaringType.Name, fieldName);
        return field.GetValue(instance)
            ?? throw new InvalidOperationException(
                $"{declaringType.Name}.{fieldName} was not initialized.");
    }

    private static bool KeysMatch(BeatmapKey actual, BeatmapKey expected)
    {
        return actual.levelId == expected.levelId &&
               actual.difficulty == expected.difficulty &&
               actual.beatmapCharacteristic != null &&
               expected.beatmapCharacteristic != null &&
               actual.beatmapCharacteristic.serializedName ==
               expected.beatmapCharacteristic.serializedName;
    }

    private static string DescribeKey(BeatmapKey key)
    {
        return $"{key.beatmapCharacteristic?.serializedName ?? "<none>"}/{key.difficulty}";
    }

    private static BeatmapKey FindBeatmapKey(
        BeatmapLevel level,
        BeatLocator.WebUtils.BLWebUtil.DifficultyEntry selectedDifficulty)
    {
        if (!TryParseDifficulty(selectedDifficulty.DifficultyName, out var targetDifficulty))
        {
            throw new InvalidOperationException(
                $"Unknown ranked-map difficulty: {selectedDifficulty.DifficultyName ?? "<null>"}.");
        }

        var keys = level.GetBeatmapKeys();
        var matchingDifficultyKeys = keys
            .Where(key => key.difficulty == targetDifficulty)
            .ToArray();
        var targetMode = NormalizeModeName(selectedDifficulty.ModeName);

        var exactKey = matchingDifficultyKeys.FirstOrDefault(key =>
            NormalizeModeName(key.beatmapCharacteristic.serializedName) == targetMode);
        if (exactKey.beatmapCharacteristic != null)
        {
            return exactKey;
        }

        var availableKeys = string.Join(
            ", ",
            keys.Select(key =>
                $"{key.beatmapCharacteristic.serializedName}/{key.difficulty}"));
        throw new InvalidOperationException(
            $"The selected beatmap {selectedDifficulty.ModeName}/" +
            $"{selectedDifficulty.DifficultyName} was not found. Available: {availableKeys}.");
    }

    private static bool TryParseDifficulty(
        string? difficultyName,
        out BeatmapDifficulty difficulty)
    {
        var normalized = NormalizeToken(difficultyName).Replace("+", "plus");
        switch (normalized)
        {
            case "easy":
                difficulty = BeatmapDifficulty.Easy;
                return true;
            case "normal":
                difficulty = BeatmapDifficulty.Normal;
                return true;
            case "hard":
                difficulty = BeatmapDifficulty.Hard;
                return true;
            case "expert":
                difficulty = BeatmapDifficulty.Expert;
                return true;
            case "expertplus":
                difficulty = BeatmapDifficulty.ExpertPlus;
                return true;
            default:
                difficulty = default;
                return false;
        }
    }

    private static string NormalizeModeName(string? modeName)
    {
        var normalized = NormalizeToken(modeName);

        if (normalized.Contains("standard")) return "standard";
        if (normalized.Contains("360") || normalized.Contains("threesixty")) return "360";
        if (normalized.Contains("90") || normalized.Contains("ninety")) return "90";
        if (normalized.Contains("onesaber")) return "onesaber";
        if (normalized.Contains("noarrows")) return "noarrows";
        if (normalized.Contains("lawless")) return "lawless";
        if (normalized.Contains("lightshow")) return "lightshow";

        return normalized;
    }

    private static string NormalizeToken(string? value)
    {
        if (value is not string nonNullValue || string.IsNullOrWhiteSpace(nonNullValue))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(nonNullValue.Length);
        foreach (var character in nonNullValue)
        {
            if (char.IsLetterOrDigit(character) || character == '+')
            {
                builder.Append(char.ToLowerInvariant(character));
            }
        }

        return builder.ToString();
    }
}
