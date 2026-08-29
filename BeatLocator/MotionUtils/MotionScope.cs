using System;
using System.Collections.Generic;
using Tweening;
using UnityEngine;
using UnityEngine.UI;

namespace MotionUtils;

internal sealed class MotionScope : MonoBehaviour
{
    private readonly Dictionary<string, Tween> _channels = new();
    private readonly Dictionary<string, MotionSequenceRun> _sequences = new();
    private UnscaledTweeningManager _manager = null!;
    private Component? _owner;
    private int _nextSequenceId;

    private void Awake()
    {
        _manager = GetComponent<UnscaledTweeningManager>() ??
                   gameObject.AddComponent<UnscaledTweeningManager>();
    }

    internal void Bind(Component owner)
    {
        if (_owner != null && _owner != owner)
        {
            throw new InvalidOperationException(
                "A motion scope cannot be rebound to another owner.");
        }

        _owner = owner;
    }

    internal bool IsOwnedBy(Component owner)
    {
        return _owner == owner;
    }

    internal MotionHandle Value(
        string channel,
        float from,
        float to,
        Action<float> setter,
        MotionSpec spec,
        Action? completed = null,
        Action? killed = null)
    {
        if (string.IsNullOrWhiteSpace(channel))
        {
            throw new ArgumentException(
                "A motion channel name is required.",
                nameof(channel));
        }

        if (setter == null)
        {
            throw new ArgumentNullException(nameof(setter));
        }

        Replace(channel);
        if (spec.Duration <= 0f && spec.Delay <= 0f)
        {
            setter(to);
            completed?.Invoke();
            return default;
        }

        var tween = new FloatTween(
            from,
            to,
            setter,
            Mathf.Max(0f, spec.Duration),
            spec.Ease,
            Mathf.Max(0f, spec.Delay));
        Register(channel, tween, completed, killed);
        return new MotionHandle(this, channel, tween);
    }

    internal MotionHandle Delay(
        string channel,
        float duration,
        Action completed)
    {
        if (completed == null)
        {
            throw new ArgumentNullException(nameof(completed));
        }

        return Value(
            channel,
            0f,
            1f,
            _ => { },
            new MotionSpec(Mathf.Max(0f, duration), EaseType.Linear),
            completed);
    }

    internal MotionHandle Fade(
        string channel,
        CanvasGroup target,
        float alpha,
        MotionSpec spec,
        Action? completed = null)
    {
        return Value(
            channel,
            target.alpha,
            alpha,
            value =>
            {
                if (target)
                {
                    target.alpha = value;
                }
            },
            spec,
            completed);
    }

    internal MotionHandle Scale(
        string channel,
        Transform target,
        Vector3 scale,
        MotionSpec spec)
    {
        Replace(channel);
        var tween = new Vector3Tween(
            target.localScale,
            scale,
            value =>
            {
                if (target)
                {
                    target.localScale = value;
                }
            },
            Mathf.Max(0f, spec.Duration),
            spec.Ease,
            Mathf.Max(0f, spec.Delay));
        Register(channel, tween);
        return new MotionHandle(this, channel, tween);
    }

    internal MotionHandle Vector2Value(
        string channel,
        Vector2 from,
        Vector2 to,
        Action<Vector2> setter,
        MotionSpec spec,
        Action? completed = null,
        Action? killed = null)
    {
        if (setter == null)
        {
            throw new ArgumentNullException(nameof(setter));
        }

        Replace(channel);
        if (spec.Duration <= 0f && spec.Delay <= 0f)
        {
            setter(to);
            completed?.Invoke();
            return default;
        }

        var tween = new Vector2Tween(
            from,
            to,
            setter,
            Mathf.Max(0f, spec.Duration),
            spec.Ease,
            Mathf.Max(0f, spec.Delay));
        Register(channel, tween, completed, killed);
        return new MotionHandle(this, channel, tween);
    }

    internal MotionHandle Move(
        string channel,
        RectTransform target,
        Vector2 anchoredPosition,
        MotionSpec spec)
    {
        return Vector2Value(
            channel,
            target.anchoredPosition,
            anchoredPosition,
            value =>
            {
                if (target)
                {
                    target.anchoredPosition = value;
                }
            },
            spec);
    }

    internal MotionHandle Tint(
        string channel,
        Graphic target,
        Color color,
        MotionSpec spec)
    {
        Replace(channel);
        var tween = new ColorTween(
            target.color,
            color,
            value =>
            {
                if (target)
                {
                    target.color = value;
                }
            },
            Mathf.Max(0f, spec.Duration),
            spec.Ease,
            Mathf.Max(0f, spec.Delay));
        Register(channel, tween);
        return new MotionHandle(this, channel, tween);
    }

    internal MotionSequence Sequence(string channel)
    {
        if (string.IsNullOrWhiteSpace(channel))
        {
            throw new ArgumentException(
                "A sequence channel name is required.",
                nameof(channel));
        }

        return new MotionSequence(this, channel);
    }

    internal void Kill(string channel)
    {
        KillSequence(channel);
        if (_channels.TryGetValue(channel, out var tween))
        {
            Kill(channel, tween);
        }
    }

    internal void Kill(string channel, Tween tween)
    {
        if (_channels.TryGetValue(channel, out var current) &&
            ReferenceEquals(current, tween))
        {
            _channels.Remove(channel);
            tween.Kill();
        }
    }

    private void Register(
        string channel,
        Tween tween,
        Action? completed = null,
        Action? killed = null)
    {
        _channels[channel] = tween;
        tween.onCompleted += () =>
        {
            Forget(channel, tween);
            completed?.Invoke();
        };
        tween.onKilled += () =>
        {
            Forget(channel, tween);
            killed?.Invoke();
        };
        _manager.AddTween(tween, this, false);
    }

    internal MotionSequenceHandle StartSequence(
        string channel,
        IReadOnlyList<MotionSequenceOperation> operations,
        Action? completed)
    {
        Kill(channel);
        var run = new MotionSequenceRun(
            this,
            channel,
            ++_nextSequenceId,
            operations,
            completed);
        _sequences[channel] = run;
        run.Start();
        return new MotionSequenceHandle(run);
    }

    internal void CompleteSequence(
        string channel,
        MotionSequenceRun run,
        Action? completed)
    {
        if (_sequences.TryGetValue(channel, out var current) &&
            ReferenceEquals(current, run))
        {
            _sequences.Remove(channel);
            completed?.Invoke();
        }
    }

    internal void ForgetSequence(
        string channel,
        MotionSequenceRun run)
    {
        if (_sequences.TryGetValue(channel, out var current) &&
            ReferenceEquals(current, run))
        {
            _sequences.Remove(channel);
        }
    }

    private void KillSequence(string channel)
    {
        if (_sequences.TryGetValue(channel, out var run))
        {
            _sequences.Remove(channel);
            run.Kill();
        }
    }

    private void Replace(string channel)
    {
        if (_channels.TryGetValue(channel, out var current))
        {
            _channels.Remove(channel);
            current.Kill();
        }
    }

    private void Forget(string channel, Tween tween)
    {
        if (_channels.TryGetValue(channel, out var current) &&
            ReferenceEquals(current, tween))
        {
            _channels.Remove(channel);
        }
    }

    private void OnDisable()
    {
        KillAll();
    }

    private void OnDestroy()
    {
        KillAll();
    }

    private void KillAll()
    {
        var sequenceRuns = new List<MotionSequenceRun>(_sequences.Values);
        _sequences.Clear();
        foreach (var sequenceRun in sequenceRuns)
        {
            sequenceRun.Kill();
        }

        if (_manager != null)
        {
            _manager.KillAllTweens(this);
        }

        _channels.Clear();
    }
}
