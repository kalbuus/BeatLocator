using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;

namespace BeatLocator.Dialogue;

internal sealed class BotDialogueService
{
    private const int SupportedSchemaVersion = 1;
    private const string BotLocatorResource =
        "BeatLocator.Assets.Avatars.BotLocator.json";
    private const string FallbackAvatarId = "BotLocator";
    private const string FallbackEmotion = "default";
    private const string FallbackSpriteResource =
        "BeatLocator.Assets.botlocator_default.png";

    private readonly Random _random = new Random();
    private BotAvatarDefinition? _definition;
    private string? _lastSelectionKey;
    private bool _loadAttempted;

    internal string DefaultSpriteResource
    {
        get
        {
            EnsureLoaded();
            return ResolveSpriteResource(
                _definition?.DefaultEmotion ?? FallbackEmotion);
        }
    }

    internal bool TrySelect(
        BotDialogueContext context,
        out BotDialoguePresentation? presentation)
    {
        if (context == null)
        {
            throw new ArgumentNullException(nameof(context));
        }

        EnsureLoaded();
        if (_definition == null)
        {
            presentation = null;
            return false;
        }

        var candidates = BuildCandidates(context);
        if (candidates.Count == 0)
        {
            Plugin.Log.Debug(
                $"[Bot Dialogue] No authored line for event={context.Event}, " +
                $"provider={context.Provider}.");
            presentation = null;
            return false;
        }

        var maximumSpecificity = candidates.Max(candidate => candidate.Specificity);
        candidates.RemoveAll(candidate => candidate.Specificity != maximumSpecificity);
        var maximumPriority = candidates.Max(candidate => candidate.Entry.Priority);
        candidates.RemoveAll(candidate => candidate.Entry.Priority != maximumPriority);

        if (candidates.Count > 1 && !string.IsNullOrEmpty(_lastSelectionKey))
        {
            var withoutRepeat = candidates
                .Where(candidate => candidate.SelectionKey != _lastSelectionKey)
                .ToList();
            if (withoutRepeat.Count > 0)
            {
                candidates = withoutRepeat;
            }
        }

        var selected = SelectWeighted(candidates);
        _lastSelectionKey = selected.SelectionKey;
        var emotion = string.IsNullOrWhiteSpace(selected.Entry.Emotion)
            ? _definition.DefaultEmotion
            : selected.Entry.Emotion;
        if (!_definition.Emotions.ContainsKey(emotion))
        {
            Plugin.Log.Warn(
                $"[Bot Dialogue] Line '{selected.Entry.Id}' references unknown " +
                $"emotion '{emotion}'. Using '{_definition.DefaultEmotion}'.");
            emotion = _definition.DefaultEmotion;
        }

        presentation = new BotDialoguePresentation(
            _definition.Id,
            selected.Entry.Id,
            emotion,
            ResolveSpriteResource(emotion),
            selected.Text);
        Plugin.Log.Debug(
            $"[Bot Dialogue] avatar={presentation.AvatarId}, " +
            $"event={context.Event}, line={presentation.LineId}, " +
            $"emotion={presentation.Emotion}, specificity={selected.Specificity}.");
        return true;
    }

    private List<Candidate> BuildCandidates(BotDialogueContext context)
    {
        var candidates = new List<Candidate>();
        foreach (var entry in _definition!.Dialogue)
        {
            if (!string.Equals(
                    entry.Event,
                    context.Event,
                    StringComparison.OrdinalIgnoreCase) ||
                entry.When?.Matches(context) == false)
            {
                continue;
            }

            var texts = entry.Texts?
                .Where(text => !string.IsNullOrWhiteSpace(text))
                .Select(text => text.Trim())
                .Distinct(StringComparer.Ordinal)
                .ToArray() ?? Array.Empty<string>();
            foreach (var text in texts)
            {
                candidates.Add(new Candidate(
                    entry,
                    text,
                    entry.When?.GetSpecificity() ?? 0));
            }
        }

        return candidates;
    }

    private Candidate SelectWeighted(IReadOnlyList<Candidate> candidates)
    {
        var totalWeight = candidates.Sum(candidate => candidate.Weight);
        var target = _random.NextDouble() * totalWeight;
        foreach (var candidate in candidates)
        {
            target -= candidate.Weight;
            if (target <= 0d)
            {
                return candidate;
            }
        }

        return candidates[candidates.Count - 1];
    }

    private string ResolveSpriteResource(string emotion)
    {
        if (_definition != null &&
            _definition.Emotions.TryGetValue(emotion, out var resourceName) &&
            ResourceExists(resourceName))
        {
            return resourceName;
        }

        return FallbackSpriteResource;
    }

    private void EnsureLoaded()
    {
        if (_loadAttempted)
        {
            return;
        }

        _loadAttempted = true;
        try
        {
            var assembly = typeof(BotDialogueService).Assembly;
            using var stream = assembly.GetManifestResourceStream(BotLocatorResource);
            if (stream == null)
            {
                Plugin.Log.Error(
                    $"[Bot Dialogue] Embedded avatar file '{BotLocatorResource}' " +
                    "was not found.");
                return;
            }

            using var reader = new StreamReader(stream);
            var definition = JsonConvert.DeserializeObject<BotAvatarDefinition>(
                reader.ReadToEnd());
            if (!Validate(definition))
            {
                return;
            }

            _definition = definition;
            var authoredCount = definition!.Dialogue.Sum(entry =>
                entry.Texts?.Count(text => !string.IsNullOrWhiteSpace(text)) ?? 0);
            Plugin.Log.Info(
                $"[Bot Dialogue] Loaded avatar '{definition.Id}' with " +
                $"{definition.Emotions.Count} emotion(s) and " +
                $"{authoredCount} authored line(s).");
        }
        catch (Exception exception)
        {
            Plugin.Log.Error(
                $"[Bot Dialogue] Could not load '{BotLocatorResource}': " +
                exception);
        }
    }

    private static bool Validate(BotAvatarDefinition? definition)
    {
        if (definition == null)
        {
            Plugin.Log.Error("[Bot Dialogue] BotLocator JSON is empty or invalid.");
            return false;
        }

        if (definition.SchemaVersion != SupportedSchemaVersion)
        {
            Plugin.Log.Error(
                $"[Bot Dialogue] Unsupported schemaVersion " +
                $"{definition.SchemaVersion}; expected {SupportedSchemaVersion}.");
            return false;
        }

        if (string.IsNullOrWhiteSpace(definition.Id) ||
            string.IsNullOrWhiteSpace(definition.DefaultEmotion) ||
            definition.Emotions == null ||
            !definition.Emotions.ContainsKey(definition.DefaultEmotion))
        {
            Plugin.Log.Error(
                "[Bot Dialogue] Avatar id, defaultEmotion, or its sprite mapping " +
                "is missing.");
            return false;
        }

        definition.Dialogue ??= new List<BotDialogueEntry>();
        var duplicateId = definition.Dialogue
            .Where(entry => !string.IsNullOrWhiteSpace(entry.Id))
            .GroupBy(entry => entry.Id, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicateId != null)
        {
            Plugin.Log.Error(
                $"[Bot Dialogue] Duplicate line id '{duplicateId.Key}'.");
            return false;
        }

        definition.Dialogue.RemoveAll(entry =>
        {
            var invalid = string.IsNullOrWhiteSpace(entry.Id) ||
                          string.IsNullOrWhiteSpace(entry.Event);
            if (invalid)
            {
                Plugin.Log.Warn(
                    "[Bot Dialogue] Ignoring an entry without id or event.");
            }
            return invalid;
        });
        return true;
    }

    private static bool ResourceExists(string? resourceName)
    {
        return !string.IsNullOrWhiteSpace(resourceName) &&
               typeof(BotDialogueService).Assembly
                   .GetManifestResourceInfo(resourceName) != null;
    }

    private sealed class Candidate
    {
        internal Candidate(
            BotDialogueEntry entry,
            string text,
            int specificity)
        {
            Entry = entry;
            Text = text;
            Specificity = specificity;
            Weight = entry.Weight > 0f ? entry.Weight : 1f;
            SelectionKey = entry.Id + "\n" + text;
        }

        internal BotDialogueEntry Entry { get; }
        internal string Text { get; }
        internal int Specificity { get; }
        internal float Weight { get; }
        internal string SelectionKey { get; }
    }
}
