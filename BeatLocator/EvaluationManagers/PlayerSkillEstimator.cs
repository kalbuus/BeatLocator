using System;
using System.Collections.Generic;
using System.Linq;

namespace BeatLocator.EvaluationManagers;

/// <summary>Pure, deterministic estimation of demonstrated skill from accumulated results.</summary>
internal static class PlayerSkillEstimator
{
    internal const float MinimumStars = 0.1f;
    internal const float MaximumStars = 25f;
    private const double DefaultSlope = -0.35;
    private const double ComfortableAccuracy = 0.90;

    internal readonly struct Sample
    {
        internal Sample(PlayerScoreEntry score, float stars, float? acc, float? pass, float? tech, double weight)
        { Score = score; Stars = stars; Acc = acc; Pass = pass; Tech = tech; Weight = weight; }
        internal PlayerScoreEntry Score { get; }
        internal float Stars { get; }
        internal float? Acc { get; }
        internal float? Pass { get; }
        internal float? Tech { get; }
        internal double Weight { get; }
    }

    internal sealed class Profile
    {
        internal float Center { get; set; }
        internal double Slope { get; set; }
        internal float AccBudget { get; set; }
        internal float PassBudget { get; set; }
        internal float TechBudget { get; set; }
        internal float RecentChallenge { get; set; }
        internal float ChallengeBoundary { get; set; }
        internal double ReferenceAccuracy { get; set; } = ComfortableAccuracy;
        internal int SampleCount { get; set; }
        internal int SkippedCount { get; set; }
        internal int AgedOutCount { get; set; }
        internal int SpeedCount { get; set; }
        internal string Confidence { get; set; } = "fallback";
        internal PlayerScoreEntry[] Evidence { get; set; } = Array.Empty<PlayerScoreEntry>();
        internal DateTimeOffset EstimatedAt { get; set; }

        internal float Target(int preset, bool endMePossible)
        {
            if (preset == 4)
            {
                var boundary = Math.Max(Center, ChallengeBoundary);
                var step = Math.Min(endMePossible ? 0.75f : 1.5f,
                    Math.Max(0.06f, boundary * (endMePossible ? 0.1f : 0.2f)));
                // A weak player's harder range is not represented by a tiny absolute
                // gap. Keep the possible challenge conservative; stretch Impossible.
                if (!endMePossible)
                {
                    var stretch = 0.2f + 0.2f * Math.Max(0f, Math.Min(1f, (8f - boundary) / 3f));
                    step = Math.Max(step, Math.Min(1.5f, boundary * 0.2f) + boundary * (stretch - 0.2f));
                }
                return Bound(boundary + step);
            }
            var accuracy = preset switch
            {
                0 => Math.Min(0.97, ReferenceAccuracy + 0.07),
                1 => Math.Min(0.94, ReferenceAccuracy + 0.04),
                2 => ReferenceAccuracy, 3 => ReferenceAccuracy - 0.05,
                _ => throw new ArgumentOutOfRangeException(nameof(preset))
            };
            var maxDistance = preset == 0
                ? Math.Min(3f, Math.Max(0.12f, Center * 0.5f))
                : Math.Min(1.75f, Math.Max(0.12f, Center * 0.25f));
            var offset = (Logit(accuracy) - Logit(ReferenceAccuracy)) / Slope;
            var target = Center + (float)Math.Max(-maxDistance, Math.Min(maxDistance, offset));
            if (preset == 3) target = Math.Min(target,
                Math.Max(Center, ChallengeBoundary) + Math.Min(0.35f, Math.Max(0.03f, ChallengeBoundary * 0.05f)));
            return Bound(target);
        }

        internal float Suitability(RecommendationDifficulty difficulty, float target)
        {
            var scale = Math.Max(0.5f, target / Center);
            var result = 1f;
            // Accuracy specialization must not become an unsupported pass/tech challenge.
            foreach (var pair in new[] { (difficulty.PassRating, PassBudget), (difficulty.TechRating, TechBudget) })
            {
                if (!pair.Item1.HasValue || pair.Item2 <= 0) continue;
                var ratio = pair.Item1.Value / (pair.Item2 * scale);
                if (ratio > 1.6f) return 0f;
                if (ratio > 1.1f) result *= (1.6f - ratio) / 0.5f;
            }
            if (difficulty.AccRating.HasValue && AccBudget > 0)
            {
                var ratio = difficulty.AccRating.Value / (AccBudget * scale);
                if (ratio > 1.6f) return 0f;
                if (ratio > 1.15f) result *= (1.6f - ratio) / 0.45f;
            }
            return Math.Max(0f, result);
        }
    }

    internal static bool TryNormalize(PlayerScoreEntry score, out Sample sample, double weight = 1)
    {
        sample = default;
        if (score.Failed) return false;
        var d = score.Leaderboard?.Difficulty;
        if (d == null || !Valid(d.Stars) || d.Stars <= 0 ||
            !Finite(score.Accuracy) || (!score.Failed && (score.Accuracy <= 0 || score.Accuracy > 1))) return false;
        var mods = (score.Modifiers ?? string.Empty).Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(m => m.Trim().ToUpperInvariant()).ToArray();
        // Cosmetic/energy restrictions leave accuracy and map geometry comparable.
        // Geometry-changing and unknown mods are explicitly excluded, not approximated by a PP multiplier.
        if (mods.Any(m => m != "FS" && m != "SF" && m != "SS" && m != "DA" &&
                          m != "GN" && m != "IF" && m != "BE" && m != "SA")) return false;
        var speeds = mods.Where(m => m == "FS" || m == "SF" || m == "SS").ToArray();
        if (speeds.Length > 1) return false;
        var stars = d.Stars!.Value;
        var acc = d.AccRating; var pass = d.PassRating; var tech = d.TechRating;
        if (speeds.Length == 1)
        {
            var prefix = speeds[0].ToLowerInvariant();
            if (!TryRating(d.ModifiersRating, prefix + "Stars", out var speedStars)) return false;
            stars = speedStars;
            acc = Rating(d.ModifiersRating, prefix + "AccRating");
            pass = Rating(d.ModifiersRating, prefix + "PassRating");
            tech = Rating(d.ModifiersRating, prefix + "TechRating");
        }
        sample = new Sample(score, stars, acc, pass, tech, weight);
        return true;
    }

    internal static Profile Estimate(IEnumerable<PlayerScoreEntry> source, DateTimeOffset now)
    {
        var profile = new Profile();
        var ordered = source.Where(s => s.Timepost > 0 && s.Timepost <= now.ToUnixTimeSeconds() + 86400)
            .OrderByDescending(s => s.Timepost).Take(240).ToArray();
        var samples = new List<Sample>();
        var newestTime = ordered.Length == 0 ? 0 : ordered[0].Timepost;
        for (var i = 0; i < ordered.Length; i++)
        {
            var score = ordered[i];
            // Exponential decay has no permanent accuracy-weight floor. Top scores also age.
            var daysBehindNewest = Math.Max(0, (newestTime - (double)score.Timepost) / 86400);
            var weight = Math.Pow(0.5, daysBehindNewest / 45) * Math.Pow(0.5, i / 60d);
            if (!TryNormalize(score, out var sample, weight)) { profile.SkippedCount++; continue; }
            if (sample.Weight < 0.001) { profile.AgedOutCount++; continue; }
            samples.Add(sample);
            if (score.Modifiers.Contains("FS") || score.Modifiers.Contains("SF") || score.Modifiers.Contains("SS")) profile.SpeedCount++;
        }
        if (samples.Count == 0) throw new ArgumentException("No comparable ranked results with difficulty ratings are available.");
        profile.Evidence = ordered;
        profile.EstimatedAt = now;
        var ordinaryEvidence = samples.ToList();
        // Easy practice carries little information about the player's harder range.
        // Weak evidence from easy practice must not dominate an API rebuild.
        var anchor = Quantile(samples.Where(s => !s.Score.Failed).Select(s => (s.Stars, s.Weight)), 0.9);
        samples = samples.Select(s => new Sample(s.Score, s.Stars, s.Acc, s.Pass, s.Tech,
            s.Weight * (s.Score.Failed ? 1 : Math.Max(0.01, Math.Pow(Math.Min(1, s.Stars / anchor), 3))))).ToList();
        var recent = samples.Take(60).ToList();
        var accuracyEvidence = ordinaryEvidence.ToArray();
        profile.ReferenceAccuracy = accuracyEvidence.Length == 0 ? ComfortableAccuracy :
            Math.Max(0.80, Math.Min(ComfortableAccuracy,
                accuracyEvidence.Sum(s => s.Score.Accuracy * s.Weight) / accuracyEvidence.Sum(s => s.Weight) + 0.02));
        profile.SampleCount = samples.Count;
        profile.Slope = FitSlope(samples, out var confidence);
        profile.Confidence = confidence;
        var challengeEvidence = recent.Where(s => !s.Score.Failed && s.Score.Accuracy >= 0.70f).ToArray();
        profile.RecentChallenge = challengeEvidence.Length == 0 ? 0 :
            Quantile(challengeEvidence.Select(s => (s.Stars, s.Weight)), 0.85);
        // OKAY describes the usual range, not an upper capability percentile.
        // Accuracy supplies only a small reserve above played difficulty; difficulty
        // specialization and the harder tail remain available to END ME separately.
        profile.Center = Bound((float)(ordinaryEvidence.Sum(s => s.Weight *
            (s.Stars + Math.Min(0.75, Capability(s, profile.Slope, profile.ReferenceAccuracy) - s.Stars))) /
            ordinaryEvidence.Sum(s => s.Weight)));
        // Only a small reserve may be inferred above recent hard clears. High accuracy
        // on an easy map is not proof of unlimited pass/tech capability.
        profile.ChallengeBoundary = challengeEvidence.Length == 0 ? profile.Center :
            Quantile(challengeEvidence.Select(s => (s.Stars +
                (float)Math.Max(0, Math.Min(Math.Min(0.75, s.Stars * 0.15),
                    (Logit(s.Score.Accuracy) - Logit(profile.ReferenceAccuracy)) / -profile.Slope)), s.Weight)), 0.85);
        profile.Center = Math.Min(profile.Center, Bound(profile.ChallengeBoundary));
        profile.AccBudget = ComponentBudget(recent, profile.Slope, s => s.Acc, profile.ReferenceAccuracy);
        profile.PassBudget = ComponentBudget(recent, profile.Slope, s => s.Pass, profile.ReferenceAccuracy);
        profile.TechBudget = ComponentBudget(recent, profile.Slope, s => s.Tech, profile.ReferenceAccuracy);
        return profile;
    }

    internal static Profile Observe(Profile previous, PlayerScoreEntry score)
    {
        if (!TryNormalize(score, out _)) return previous;
        // A local clear is ordinary history evidence, never a separate per-play delta.
        return Estimate(previous.Evidence.Concat(new[] { score }), previous.EstimatedAt);
    }
    internal static bool TryEvaluationDifficulty(RecommendationDifficulty difficulty, string speed,
        out RecommendationDifficulty effective)
    {
        effective = difficulty;
        if (speed.Length == 0) return Valid(difficulty.Stars);
        var score = new PlayerScoreEntry
        {
            Accuracy = 0.94f, Modifiers = speed,
            Leaderboard = new ScoreLeaderboard { Difficulty = new ScoreDifficulty
                { Stars = difficulty.Stars, AccRating = difficulty.AccRating, PassRating = difficulty.PassRating,
                  TechRating = difficulty.TechRating, ModifiersRating = difficulty.ModifiersRating } }
        };
        if (!TryNormalize(score, out var sample)) return false;
        effective = new RecommendationDifficulty
            { Stars = sample.Stars, AccRating = sample.Acc, PassRating = sample.Pass, TechRating = sample.Tech };
        return true;
    }

    internal static float Capability(Sample sample, double slope, double referenceAccuracy = ComfortableAccuracy)
    {
        if (sample.Score.Failed) return Bound(sample.Stars * 0.7f);
        var delta = (Logit(sample.Score.Accuracy) - Logit(referenceAccuracy)) / -slope;
        // Never infer unlimited skill from one tiny map or an accuracy outlier.
        delta = Math.Max(-Math.Min(4, sample.Stars * 0.65), Math.Min(Math.Min(2.5, sample.Stars * 0.45), delta));
        return Bound(sample.Stars + (float)delta);
    }

    private static double FitSlope(List<Sample> samples, out string confidence)
    {
        var cleared = samples.Where(s => !s.Score.Failed).ToArray();
        confidence = "fallback";
        if (cleared.Length < 5) return DefaultSlope;
        var sw = cleared.Sum(s => s.Weight);
        var x = cleared.Sum(s => s.Stars * s.Weight) / sw;
        var y = cleared.Sum(s => Logit(s.Score.Accuracy) * s.Weight) / sw;
        var variance = cleared.Sum(s => s.Weight * (s.Stars - x) * (s.Stars - x));
        if (variance / sw < 0.25) return DefaultSlope;
        var slope = cleared.Sum(s => s.Weight * (s.Stars - x) * (Logit(s.Score.Accuracy) - y)) / variance;
        if (slope >= -0.03 || double.IsNaN(slope)) return DefaultSlope;
        confidence = "regularized";
        var strength = Math.Min(0.35, cleared.Length / 100d);
        return Math.Max(-0.8, Math.Min(-0.15, slope * strength + DefaultSlope * (1 - strength)));
    }

    private static float ComponentBudget(List<Sample> samples, double slope, Func<Sample, float?> component, double referenceAccuracy)
    {
        var values = samples.Where(s => !s.Score.Failed && Valid(component(s)))
            .Select(s => (component(s)!.Value * Math.Max(0.6f, Math.Min(1.3f, Capability(s, slope, referenceAccuracy) / s.Stars)), s.Weight)).ToArray();
        return values.Length < 3 ? 0 : (float)(values.Sum(v => v.Item1 * v.Weight) / values.Sum(v => v.Weight));
    }

    private static float Quantile(IEnumerable<(float Value, double Weight)> values, double percentile)
    {
        var sorted = values.OrderBy(v => v.Value).ToArray();
        var remaining = sorted.Sum(v => v.Weight) * percentile;
        foreach (var value in sorted) { remaining -= value.Weight; if (remaining <= 0) return value.Value; }
        return sorted[sorted.Length - 1].Value;
    }
    private static double Logit(double accuracy)
    { var a = Math.Max(0.01, Math.Min(0.9995, accuracy)); return Math.Log(a / (1 - a)); }
    internal static float Bound(float stars) => Math.Max(MinimumStars, Math.Min(MaximumStars, stars));
    private static bool Finite(float n) => !float.IsNaN(n) && !float.IsInfinity(n);
    private static bool Valid(float? n) => n.HasValue && Finite(n.Value) && n > 0;
    private static float? Rating(Dictionary<string, float>? ratings, string key) => TryRating(ratings, key, out var n) ? n : (float?)null;
    private static bool TryRating(Dictionary<string, float>? ratings, string key, out float n)
    {
        n = 0;
        if (ratings == null) return false;
        foreach (var pair in ratings)
            if (string.Equals(pair.Key, key, StringComparison.OrdinalIgnoreCase) && Valid(pair.Value)) { n = pair.Value; return true; }
        return false;
    }
}

