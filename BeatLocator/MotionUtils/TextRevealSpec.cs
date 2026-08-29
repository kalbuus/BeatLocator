using UnityEngine;

namespace MotionUtils;

internal enum TextRevealStyle
{
    Fade,
    Typewriter,
    Rise,
    Jump
}

internal readonly struct TextRevealSpec
{
    internal TextRevealSpec(
        TextRevealStyle style,
        float duration,
        EaseType ease = EaseType.OutCubic,
        Vector2 offset = default,
        float startScale = 0.82f)
    {
        Style = style;
        Duration = duration;
        Ease = ease;
        Offset = offset;
        StartScale = startScale;
    }

    internal TextRevealStyle Style { get; }
    internal float Duration { get; }
    internal EaseType Ease { get; }
    internal Vector2 Offset { get; }
    internal float StartScale { get; }

    internal static TextRevealSpec Fade(float duration)
    {
        return new TextRevealSpec(TextRevealStyle.Fade, duration);
    }

    internal static TextRevealSpec Typewriter(float duration)
    {
        return new TextRevealSpec(
            TextRevealStyle.Typewriter,
            duration,
            EaseType.Linear);
    }

    internal static TextRevealSpec Rise(
        float duration,
        float distance = 2f)
    {
        return new TextRevealSpec(
            TextRevealStyle.Rise,
            duration,
            EaseType.OutCubic,
            new Vector2(0f, distance));
    }

    internal static TextRevealSpec Jump(
        float duration,
        float distance = -1.5f,
        float startScale = 0.82f)
    {
        return new TextRevealSpec(
            TextRevealStyle.Jump,
            duration,
            EaseType.OutBack,
            new Vector2(0f, distance),
            startScale);
    }
}
