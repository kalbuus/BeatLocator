using System;
using System.Collections.Generic;

namespace BeatLocator.Dialogue;

internal static class BotDialogueEvents
{
    internal const string SettingsOpened = "settings.opened";
    internal const string SearchProblem = "settings.search_problem";
}

internal sealed class BotDialogueContext
{
    internal string Event { get; set; } = string.Empty;
    internal string Provider { get; set; } = string.Empty;
    internal string Visit { get; set; } = string.Empty;
    internal string PreviousOutcome { get; set; } = string.Empty;
}

internal sealed class BotAvatarDefinition
{
    public int SchemaVersion { get; set; }
    public string Id { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string DefaultEmotion { get; set; } = string.Empty;
    public Dictionary<string, string> Emotions { get; set; } =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    public List<BotDialogueEntry> Dialogue { get; set; } =
        new List<BotDialogueEntry>();
}

internal sealed class BotDialogueEntry
{
    public string Id { get; set; } = string.Empty;
    public string Event { get; set; } = string.Empty;
    public BotDialogueCondition? When { get; set; }
    public string Emotion { get; set; } = string.Empty;
    public int Priority { get; set; }
    public float Weight { get; set; } = 1f;
    public List<string> Texts { get; set; } = new List<string>();
}

internal sealed class BotDialogueCondition
{
    public string? Provider { get; set; }
    public string? Visit { get; set; }
    public string? PreviousOutcome { get; set; }

    internal bool Matches(BotDialogueContext context)
    {
        return Matches(Provider, context.Provider) &&
               Matches(Visit, context.Visit) &&
               Matches(PreviousOutcome, context.PreviousOutcome);
    }

    internal int GetSpecificity()
    {
        var specificity = 0;
        if (!string.IsNullOrWhiteSpace(Provider)) specificity++;
        if (!string.IsNullOrWhiteSpace(Visit)) specificity++;
        if (!string.IsNullOrWhiteSpace(PreviousOutcome)) specificity++;
        return specificity;
    }

    private static bool Matches(string? expected, string actual)
    {
        return string.IsNullOrWhiteSpace(expected) ||
               string.Equals(
                   expected,
                   actual,
                   StringComparison.OrdinalIgnoreCase);
    }
}

internal sealed class BotDialogueSessionState
{
    private readonly object _sync = new object();
    private string _previousOutcome = "none";
    private int _settingsVisitCount;

    internal string PreviousOutcome
    {
        get
        {
            lock (_sync)
            {
                return _previousOutcome;
            }
        }
    }

    internal void RecordCleared()
    {
        Record("cleared");
    }

    internal void RecordFailed()
    {
        Record("failed");
    }

    internal void RecordQuit()
    {
        Record("quit");
    }

    internal string RegisterSettingsVisit()
    {
        lock (_sync)
        {
            var visit = _settingsVisitCount == 0 ? "first" : "returning";
            _settingsVisitCount++;
            return visit;
        }
    }

    private void Record(string outcome)
    {
        lock (_sync)
        {
            _previousOutcome = outcome;
        }
        Plugin.Log.Debug($"[Bot Dialogue] Previous roulette outcome={outcome}.");
    }
}

internal sealed class BotDialoguePresentation
{
    internal BotDialoguePresentation(
        string avatarId,
        string lineId,
        string emotion,
        string spriteResource,
        string text)
    {
        AvatarId = avatarId;
        LineId = lineId;
        Emotion = emotion;
        SpriteResource = spriteResource;
        Text = text;
    }

    internal string AvatarId { get; }
    internal string LineId { get; }
    internal string Emotion { get; }
    internal string SpriteResource { get; }
    internal string Text { get; }
}
