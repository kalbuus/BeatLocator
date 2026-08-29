using System.Collections.Generic;
using Tweening;
using UnityEngine;

namespace MotionUtils;

internal static class UiMotion
{
    internal static MotionSequence StaggerReveal(
        this MotionSequence sequence,
        IReadOnlyList<CanvasGroup> targets,
        float interval,
        float duration,
        Vector2 offset,
        EaseType ease = EaseType.OutCubic)
    {
        var states = new List<UiRevealState>(targets.Count);
        foreach (var target in targets)
        {
            if (target)
            {
                states.Add(new UiRevealState(target, offset));
            }
        }

        return sequence.Stagger(
            states,
            interval,
            duration,
            (state, progress) => state.Apply(progress),
            ease,
            state => state.Prepare(),
            state => state.Complete(),
            state => state.Restore());
    }

    private sealed class UiRevealState
    {
        private readonly CanvasGroup _target;
        private readonly RectTransform _transform;
        private readonly Vector2 _offset;
        private float _baseAlpha;
        private Vector2 _basePosition;
        private bool _prepared;

        internal UiRevealState(CanvasGroup target, Vector2 offset)
        {
            _target = target;
            _transform = (RectTransform)target.transform;
            _offset = offset;
        }

        internal void Prepare()
        {
            if (!_target || !_transform)
            {
                return;
            }

            _baseAlpha = _target.alpha;
            _basePosition = _transform.anchoredPosition;
            _target.alpha = 0f;
            _transform.anchoredPosition = _basePosition + _offset;
            _prepared = true;
        }

        internal void Apply(float progress)
        {
            if (!_prepared || !_target || !_transform)
            {
                return;
            }

            _target.alpha = Mathf.Clamp01(progress);
            _transform.anchoredPosition = Vector2.LerpUnclamped(
                _basePosition + _offset,
                _basePosition,
                progress);
        }

        internal void Complete()
        {
            if (!_prepared || !_target || !_transform)
            {
                return;
            }

            _target.alpha = 1f;
            _transform.anchoredPosition = _basePosition;
            _prepared = false;
        }

        internal void Restore()
        {
            if (!_prepared || !_target || !_transform)
            {
                return;
            }

            _target.alpha = _baseAlpha;
            _transform.anchoredPosition = _basePosition;
            _prepared = false;
        }
    }
}
