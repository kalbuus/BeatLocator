using MotionUtils;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace BeatLocator.Menu;

internal sealed class PlatformButtonBackgroundTransition : MonoBehaviour,
    IPointerEnterHandler,
    IPointerExitHandler
{
    private const float EnabledIdleBrightness = 0.58f;
    private const float DisabledBrightness = 0.28f;
    private const float HoverBrightness = 1f;
    private const float FadeDurationSeconds = 0.18f;

    private Button _button = null!;
    private Image _background = null!;
    private float _currentBrightness;
    private float _targetBrightness;
    private bool _pointerInside;
    private MotionScope _motion = null!;

    internal void Initialize(Button button, Image background)
    {
        _button = button;
        _background = background;
        _motion = MotionUtils.Motion.For(this);
        _currentBrightness = GetTargetBrightness();
        _targetBrightness = _currentBrightness;
        ApplyBrightness();
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        _pointerInside = true;
        AnimateToTarget();
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        _pointerInside = false;
        AnimateToTarget();
    }

    private void Update()
    {
        if (_button == null || _background == null)
        {
            return;
        }

        var targetBrightness = GetTargetBrightness();
        if (Mathf.Approximately(_targetBrightness, targetBrightness))
        {
            return;
        }

        AnimateTo(targetBrightness);
    }

    private void AnimateToTarget()
    {
        if (_button != null && _background != null)
        {
            AnimateTo(GetTargetBrightness());
        }
    }

    private void AnimateTo(float targetBrightness)
    {
        _targetBrightness = targetBrightness;
        _motion.Value(
            "brightness",
            _currentBrightness,
            targetBrightness,
            value =>
            {
                _currentBrightness = value;
                ApplyBrightness();
            },
            new MotionSpec(FadeDurationSeconds));
    }

    private float GetTargetBrightness()
    {
        if (_button == null || !_button.interactable)
        {
            return DisabledBrightness;
        }

        return _pointerInside
            ? HoverBrightness
            : EnabledIdleBrightness;
    }

    private void ApplyBrightness()
    {
        _background.color = new Color(
            _currentBrightness,
            _currentBrightness,
            _currentBrightness,
            1f);
    }

    private void OnDisable()
    {
        _pointerInside = false;
        _motion?.Kill("brightness");
        _targetBrightness = _currentBrightness;
    }
}
