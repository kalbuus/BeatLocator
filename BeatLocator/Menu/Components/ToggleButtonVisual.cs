using BeatSaberMarkupLanguage.Components;
using HMUI;
using MotionUtils;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BeatLocator.Menu;

internal sealed class ToggleButtonVisual : MonoBehaviour
{
    private const float TransitionDurationSeconds = 0.11f;
    private static readonly Color InactiveBackgroundColor =
        new Color(0.42f, 0.42f, 0.42f, 1f);
    private static readonly Color ActiveBackgroundColor = Color.white;
    private static readonly Color InactiveTextColor =
        new Color(0.62f, 0.62f, 0.62f, 1f);
    private static readonly Color ActiveTextColor = Color.white;

    private NineSlicePanelRenderer _background = null!;
    private TMP_Text _label = null!;
    private float _currentState;
    private float _targetState;
    private MotionScope _motion = null!;

    internal void Initialize(
        Button button,
        TMP_Text label,
        Sprite backgroundSprite,
        Image rendererTemplate,
        bool active)
    {
        _label = label;
        _motion = MotionUtils.Motion.For(this);
        RemoveNativeVisuals(button, label);

        var backgroundObject = new GameObject(
            "NineSliceBackground",
            typeof(RectTransform));
        var backgroundRect = (RectTransform)backgroundObject.transform;
        backgroundRect.SetParent(button.transform, false);
        backgroundRect.SetAsFirstSibling();
        backgroundRect.anchorMin = Vector2.zero;
        backgroundRect.anchorMax = Vector2.one;
        backgroundRect.offsetMin = Vector2.zero;
        backgroundRect.offsetMax = Vector2.zero;
        backgroundRect.localScale = Vector3.one;

        _background = backgroundObject.AddComponent<NineSlicePanelRenderer>();
        _currentState = active ? 1f : 0f;
        _targetState = _currentState;
        _background.Initialize(
            backgroundSprite,
            rendererTemplate,
            Color.Lerp(
                InactiveBackgroundColor,
                ActiveBackgroundColor,
                _currentState));
        button.targetGraphic = CreateHitTarget(button);
        ApplyColors();
    }

    internal void SetActive(bool active)
    {
        _targetState = active ? 1f : 0f;
        if (Mathf.Approximately(_currentState, _targetState))
        {
            return;
        }

        _motion.Value(
            "active",
            _currentState,
            _targetState,
            value =>
            {
                _currentState = value;
                ApplyColors();
            },
            new MotionSpec(TransitionDurationSeconds));
    }

    private void ApplyColors()
    {
        _background.SetColor(Color.Lerp(
            InactiveBackgroundColor,
            ActiveBackgroundColor,
            _currentState));
        _label.color = Color.Lerp(
            InactiveTextColor,
            ActiveTextColor,
            _currentState);
    }

    private static void RemoveNativeVisuals(Button button, TMP_Text label)
    {
        button.transition = Selectable.Transition.None;
        button.targetGraphic = null;

        foreach (var staticAnimations in
                 button.GetComponentsInChildren<ButtonStaticAnimations>(true))
        {
            staticAnimations.enabled = false;
        }

        foreach (var spriteSwap in
                 button.GetComponentsInChildren<ButtonSpriteSwap>(true))
        {
            spriteSwap.enabled = false;
        }

        foreach (var animation in
                 button.GetComponentsInChildren<Animation>(true))
        {
            animation.Stop();
            animation.enabled = false;
        }

        foreach (var strokable in
                 button.GetComponentsInChildren<Strokable>(true))
        {
            strokable.SetType(Strokable.StrokeType.None);
            strokable.enabled = false;
        }

        foreach (var graphic in button.GetComponentsInChildren<Graphic>(true))
        {
            if (graphic == label)
            {
                graphic.raycastTarget = false;
                graphic.enabled = true;
                continue;
            }

            graphic.raycastTarget = false;
            graphic.enabled = false;
        }
    }

    private static Image CreateHitTarget(Button button)
    {
        var hitTargetObject = new GameObject(
            "HitTarget",
            typeof(RectTransform),
            typeof(CanvasRenderer),
            typeof(Image));
        var hitTargetRect =
            (RectTransform)hitTargetObject.transform;
        hitTargetRect.SetParent(button.transform, false);
        hitTargetRect.SetAsLastSibling();
        hitTargetRect.anchorMin = Vector2.zero;
        hitTargetRect.anchorMax = Vector2.one;
        hitTargetRect.offsetMin = Vector2.zero;
        hitTargetRect.offsetMax = Vector2.zero;

        var hitTarget = hitTargetObject.GetComponent<Image>();
        hitTarget.color = Color.clear;
        hitTarget.raycastTarget = true;
        return hitTarget;
    }
}
