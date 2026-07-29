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
    private bool _pointerInside;

    internal void Initialize(Button button, Image background)
    {
        _button = button;
        _background = background;
        _currentBrightness = GetTargetBrightness();
        ApplyBrightness();
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        _pointerInside = true;
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        _pointerInside = false;
    }

    private void Update()
    {
        if (_button == null || _background == null)
        {
            return;
        }

        var targetBrightness = GetTargetBrightness();
        if (Mathf.Approximately(_currentBrightness, targetBrightness))
        {
            return;
        }

        _currentBrightness = Mathf.MoveTowards(
            _currentBrightness,
            targetBrightness,
            Time.unscaledDeltaTime / FadeDurationSeconds);
        ApplyBrightness();
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
    }
}
