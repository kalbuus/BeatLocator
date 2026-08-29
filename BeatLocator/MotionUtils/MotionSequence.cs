using System;
using System.Collections.Generic;
using Tweening;

namespace MotionUtils;

internal sealed class MotionSequence
{
    private readonly MotionScope _scope;
    private readonly string _channel;
    private readonly List<MotionSequenceOperation> _operations = new();
    private float _cursor;
    private Action? _completed;
    private bool _played;

    internal MotionSequence(MotionScope scope, string channel)
    {
        _scope = scope;
        _channel = channel;
    }

    internal MotionSequence Append(
        float duration,
        Action<float> update,
        EaseType ease = EaseType.OutQuad,
        Action? prepare = null,
        Action? completed = null,
        Action? killed = null)
    {
        AddOperation(new MotionSequenceOperation(
            _cursor,
            duration,
            ease,
            update,
            prepare,
            completed,
            killed));
        return this;
    }

    internal MotionSequence AppendDelay(float duration)
    {
        var safeDuration = Math.Max(0f, duration);
        AddOperation(new MotionSequenceOperation(
            _cursor,
            safeDuration,
            EaseType.Linear,
            _ => { },
            null,
            null,
            null));
        return this;
    }

    internal MotionSequence At(
        float offset,
        float duration,
        Action<float> update,
        EaseType ease = EaseType.OutQuad,
        Action? prepare = null,
        Action? completed = null,
        Action? killed = null)
    {
        AddOperation(new MotionSequenceOperation(
            Math.Max(0f, offset),
            duration,
            ease,
            update,
            prepare,
            completed,
            killed));
        return this;
    }

    internal MotionSequence Stagger<T>(
        IReadOnlyList<T> items,
        float interval,
        float duration,
        Action<T, float> update,
        EaseType ease = EaseType.OutQuad,
        Action<T>? prepare = null,
        Action<T>? completed = null,
        Action<T>? killed = null)
    {
        var start = _cursor;
        var safeInterval = Math.Max(0f, interval);
        var safeDuration = Math.Max(0f, duration);
        for (var index = 0; index < items.Count; index++)
        {
            var item = items[index];
            At(
                start + index * safeInterval,
                safeDuration,
                progress => update(item, progress),
                ease,
                prepare == null ? null : () => prepare(item),
                completed == null ? null : () => completed(item),
                killed == null ? null : () => killed(item));
        }

        if (items.Count > 0)
        {
            _cursor = Math.Max(
                _cursor,
                start + (items.Count - 1) * safeInterval + safeDuration);
        }

        return this;
    }

    internal MotionSequence OnCompleted(Action completed)
    {
        EnsureNotPlayed();
        _completed += completed ?? throw new ArgumentNullException(
            nameof(completed));
        return this;
    }

    internal MotionSequenceHandle Play()
    {
        EnsureNotPlayed();
        _played = true;
        return _scope.StartSequence(_channel, _operations, _completed);
    }

    internal void AddOperation(MotionSequenceOperation operation)
    {
        EnsureNotPlayed();
        _operations.Add(operation);
        _cursor = Math.Max(_cursor, operation.Offset + operation.Duration);
    }

    private void EnsureNotPlayed()
    {
        if (_played)
        {
            throw new InvalidOperationException(
                "A motion sequence can only be played once.");
        }
    }
}

internal sealed class MotionSequenceOperation
{
    internal MotionSequenceOperation(
        float offset,
        float duration,
        EaseType ease,
        Action<float> update,
        Action? prepare,
        Action? completed,
        Action? killed)
    {
        Offset = Math.Max(0f, offset);
        Duration = Math.Max(0f, duration);
        Ease = ease;
        Update = update ?? throw new ArgumentNullException(nameof(update));
        Prepare = prepare;
        Completed = completed;
        Killed = killed;
    }

    internal float Offset { get; }
    internal float Duration { get; }
    internal EaseType Ease { get; }
    internal Action<float> Update { get; }
    internal Action? Prepare { get; }
    internal Action? Completed { get; }
    internal Action? Killed { get; }
}

internal sealed class MotionSequenceRun
{
    private readonly MotionScope _scope;
    private readonly string _channel;
    private readonly int _id;
    private readonly IReadOnlyList<MotionSequenceOperation> _operations;
    private readonly Action? _completed;
    private readonly List<MotionHandle> _handles = new();
    private int _remaining;

    internal MotionSequenceRun(
        MotionScope scope,
        string channel,
        int id,
        IReadOnlyList<MotionSequenceOperation> operations,
        Action? completed)
    {
        _scope = scope;
        _channel = channel;
        _id = id;
        _operations = operations;
        _completed = completed;
    }

    internal bool IsActive { get; private set; }

    internal void Start()
    {
        IsActive = true;
        _remaining = _operations.Count;
        if (_remaining == 0)
        {
            Complete();
            return;
        }

        for (var index = 0; index < _operations.Count; index++)
        {
            var operation = _operations[index];
            operation.Prepare?.Invoke();
            var operationChannel =
                $"{_channel}::sequence-{_id}::{index}";
            var handle = operation.Duration <= 0f
                ? _scope.Value(
                    operationChannel,
                    0f,
                    1f,
                    _ => { },
                    new MotionSpec(operation.Offset, EaseType.Linear),
                    () =>
                    {
                        operation.Update(1f);
                        OperationCompleted(operation);
                    },
                    operation.Killed)
                : _scope.Value(
                    operationChannel,
                    0f,
                    1f,
                    operation.Update,
                    new MotionSpec(
                        operation.Duration,
                        operation.Ease,
                        operation.Offset),
                    () => OperationCompleted(operation),
                    operation.Killed);
            _handles.Add(handle);
        }
    }

    internal void Kill()
    {
        if (!IsActive)
        {
            return;
        }

        IsActive = false;
        foreach (var handle in _handles)
        {
            handle.Kill();
        }

        _handles.Clear();
        _scope.ForgetSequence(_channel, this);
    }

    private void OperationCompleted(MotionSequenceOperation operation)
    {
        if (!IsActive)
        {
            return;
        }

        operation.Completed?.Invoke();
        _remaining--;
        if (_remaining == 0)
        {
            Complete();
        }
    }

    private void Complete()
    {
        if (!IsActive)
        {
            return;
        }

        IsActive = false;
        _handles.Clear();
        _scope.CompleteSequence(_channel, this, _completed);
    }
}
