using Tweening;

namespace MotionUtils;

internal readonly struct MotionHandle
{
    private readonly MotionScope? _scope;
    private readonly string? _channel;
    private readonly Tween? _tween;

    internal MotionHandle(
        MotionScope scope,
        string channel,
        Tween tween)
    {
        _scope = scope;
        _channel = channel;
        _tween = tween;
    }

    internal bool IsActive =>
        _tween != null && _tween.isActive && !_tween.isKilled;

    internal void Kill()
    {
        if (_scope != null && _channel != null && _tween != null)
        {
            _scope.Kill(_channel, _tween);
        }
    }
}
