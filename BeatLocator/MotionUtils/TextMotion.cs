using System;
using TMPro;
using UnityEngine;

namespace MotionUtils;

internal static class TextMotion
{
    internal static MotionHandle RevealText(
        this MotionScope scope,
        string channel,
        TMP_Text target,
        TextRevealSpec spec,
        Action? completed = null)
    {
        var state = new TextRevealState(target, spec);
        scope.Kill(channel);
        state.Prepare();
        return scope.Value(
            channel,
            0f,
            1f,
            state.Apply,
            new MotionSpec(spec.Duration, spec.Ease),
            () =>
            {
                state.Restore();
                completed?.Invoke();
            },
            state.Restore);
    }

    internal static MotionSequence RevealText(
        this MotionSequence sequence,
        TMP_Text target,
        TextRevealSpec spec,
        float offset)
    {
        var state = new TextRevealState(target, spec);
        sequence.AddOperation(new MotionSequenceOperation(
            offset,
            spec.Duration,
            spec.Ease,
            state.Apply,
            state.Prepare,
            state.Restore,
            state.Restore));
        return sequence;
    }

    private sealed class TextRevealState
    {
        private readonly TMP_Text _target;
        private readonly TextRevealSpec _spec;
        private Color _baseColor;
        private Vector2 _basePosition;
        private Vector3 _baseScale;
        private int _baseVisibleCharacters;
        private int _characterCount;
        private bool _prepared;

        internal TextRevealState(TMP_Text target, TextRevealSpec spec)
        {
            _target = target;
            _spec = spec;
        }

        internal void Prepare()
        {
            if (!_target)
            {
                return;
            }

            _baseColor = _target.color;
            _basePosition = _target.rectTransform.anchoredPosition;
            _baseScale = _target.rectTransform.localScale;
            _baseVisibleCharacters = _target.maxVisibleCharacters;
            _target.ForceMeshUpdate();
            _characterCount = _target.textInfo.characterCount;
            _prepared = true;

            switch (_spec.Style)
            {
                case TextRevealStyle.Typewriter:
                    _target.maxVisibleCharacters = 0;
                    break;
                case TextRevealStyle.Rise:
                    SetAlpha(0f);
                    _target.rectTransform.anchoredPosition =
                        _basePosition + _spec.Offset;
                    break;
                case TextRevealStyle.Jump:
                    SetAlpha(0f);
                    _target.rectTransform.anchoredPosition =
                        _basePosition + _spec.Offset;
                    _target.rectTransform.localScale =
                        _baseScale * _spec.StartScale;
                    break;
                default:
                    SetAlpha(0f);
                    break;
            }
        }

        internal void Apply(float progress)
        {
            if (!_prepared || !_target)
            {
                return;
            }

            var visibleProgress = Mathf.Clamp01(progress);
            switch (_spec.Style)
            {
                case TextRevealStyle.Typewriter:
                    _target.maxVisibleCharacters = Mathf.CeilToInt(
                        _characterCount * visibleProgress);
                    break;
                case TextRevealStyle.Rise:
                    SetAlpha(visibleProgress);
                    _target.rectTransform.anchoredPosition =
                        Vector2.LerpUnclamped(
                            _basePosition + _spec.Offset,
                            _basePosition,
                            progress);
                    break;
                case TextRevealStyle.Jump:
                    SetAlpha(visibleProgress);
                    _target.rectTransform.anchoredPosition =
                        Vector2.LerpUnclamped(
                            _basePosition + _spec.Offset,
                            _basePosition,
                            progress);
                    _target.rectTransform.localScale =
                        Vector3.LerpUnclamped(
                            _baseScale * _spec.StartScale,
                            _baseScale,
                            progress);
                    break;
                default:
                    SetAlpha(visibleProgress);
                    break;
            }
        }

        internal void Restore()
        {
            if (!_prepared || !_target)
            {
                return;
            }

            _target.color = _baseColor;
            _target.rectTransform.anchoredPosition = _basePosition;
            _target.rectTransform.localScale = _baseScale;
            _target.maxVisibleCharacters = _baseVisibleCharacters;
            _prepared = false;
        }

        private void SetAlpha(float alpha)
        {
            var color = _baseColor;
            color.a *= Mathf.Clamp01(alpha);
            _target.color = color;
        }
    }
}
