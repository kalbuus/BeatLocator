namespace MotionUtils;

internal readonly struct MotionSpec
{
    internal MotionSpec(
        float duration,
        EaseType ease = EaseType.OutQuad,
        float delay = 0f)
    {
        Duration = duration;
        Ease = ease;
        Delay = delay;
    }

    internal float Duration { get; }
    internal EaseType Ease { get; }
    internal float Delay { get; }
}
