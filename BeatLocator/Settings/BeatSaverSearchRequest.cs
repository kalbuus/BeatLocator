using System;

namespace BeatLocator.Settings;

/// <summary>Explicit map preferences; no ranking profile is needed.</summary>
internal sealed class BeatSaverSearchRequest
{
    internal static readonly string[] NpsLabels = { "ANY", "0–2", "2–4", "4–6", "6–9", "9–15" };
    internal static readonly string[] NjsLabels = { "ANY", "10–14", "14–18", "18–22", "22–30" };
    internal static readonly string[] StyleLabels = { "ANY", "BALANCED", "TECH", "SPEED", "DANCE" };
    internal static readonly string[] QualityLabels = { "ANY", "GOOD", "TOP", "CURATED" };
    private static readonly string?[] StyleTags = { null, "balanced", "tech", "speed", "dance-style" };

    internal BeatSaverSearchRequest(int nps, int njs, int style, int duration, int quality,
        bool standardOnly, bool secretDifficulty)
    {
        NpsSelection = Normalize(nps, NpsLabels.Length);
        NjsSelection = Normalize(njs, NjsLabels.Length);
        StyleSelection = Normalize(style, StyleLabels.Length);
        Duration = (SongDurationFilter)Normalize(duration, 5);
        QualitySelection = Normalize(quality, QualityLabels.Length);
        StandardOnly = standardOnly;
        SecretDifficulty = secretDifficulty;
    }

    internal int NpsSelection { get; }
    internal int NjsSelection { get; }
    internal int StyleSelection { get; }
    internal SongDurationFilter Duration { get; }
    internal int QualitySelection { get; }
    internal bool StandardOnly { get; }
    internal bool SecretDifficulty { get; }
    internal string? StyleTag => StyleTags[StyleSelection];
    internal bool CuratedOnly => QualitySelection == 3;
    internal float MinimumRating => QualitySelection == 1 ? 0.8f : QualitySelection == 2 ? 0.9f : 0f;
    internal int MinimumVotes => QualitySelection == 1 ? 10 : QualitySelection == 2 ? 30 : 0;

    internal (float? Minimum, float? Maximum) NpsRange => NpsSelection switch
    {
        1 => (0f, 2f), 2 => (2f, 4f), 3 => (4f, 6f),
        4 => (6f, 9f), 5 => (9f, 15f), _ => (null, null)
    };

    internal (float? Minimum, float? Maximum) NjsRange => NjsSelection switch
    {
        1 => (10f, 14f), 2 => (14f, 18f), 3 => (18f, 22f),
        4 => (22f, 30f), _ => (null, null)
    };

    private static int Normalize(int value, int count) => Math.Max(0, Math.Min(value, count - 1));
}
