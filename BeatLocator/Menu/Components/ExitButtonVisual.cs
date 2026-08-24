using BeatSaberMarkupLanguage.Components;
using HMUI;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace BeatLocator.Menu;

internal sealed class ExitButtonVisual : MonoBehaviour,
    IPointerEnterHandler,
    IPointerExitHandler
{
    private const float TransitionDurationSeconds = 0.14f;
    private static readonly Color IdleBackgroundColor =
        new Color(0.46f, 0.46f, 0.46f, 1f);
    private static readonly Color HoverBackgroundColor =
        new Color(1f, 0.12f, 0.12f, 1f);

    private NineSlicePanelRenderer _background = null!;
    private float _currentHover;
    private bool _pointerInside;

    internal void Initialize(
        Button button,
        Image icon,
        Sprite backgroundSprite,
        Sprite iconSprite)
    {
        RemoveNativeVisuals(button, icon);

        icon.sprite = iconSprite;
        icon.preserveAspect = true;
        icon.color = Color.white;
        icon.raycastTarget = false;
        icon.enabled = true;

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
        _background.Initialize(
            backgroundSprite,
            icon,
            IdleBackgroundColor);

        icon.rectTransform.SetAsLastSibling();
        button.targetGraphic = CreateHitTarget(button);
        _currentHover = 0f;
        _pointerInside = false;
        ApplyColor();
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
        var targetHover = _pointerInside ? 1f : 0f;
        if (Mathf.Approximately(_currentHover, targetHover))
        {
            return;
        }

        _currentHover = Mathf.MoveTowards(
            _currentHover,
            targetHover,
            Time.unscaledDeltaTime / TransitionDurationSeconds);
        ApplyColor();
    }

    private void ApplyColor()
    {
        _background.SetColor(Color.Lerp(
            IdleBackgroundColor,
            HoverBackgroundColor,
            _currentHover));
    }

    private void OnDisable()
    {
        _pointerInside = false;
        _currentHover = 0f;
        if (_background)
        {
            ApplyColor();
        }
    }

    private static void RemoveNativeVisuals(Button button, Image icon)
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
            if (graphic == icon)
            {
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
        var hitTargetRect = (RectTransform)hitTargetObject.transform;
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
