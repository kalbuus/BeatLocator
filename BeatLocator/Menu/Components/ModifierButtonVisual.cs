using BeatSaberMarkupLanguage.Components;
using HMUI;
using MotionUtils;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace BeatLocator.Menu;

/// <summary>
/// Cross-fades modifier artwork while preserving the fixed icon and text
/// geometry. Both backgrounds are nine-sliced so adaptive widths keep their
/// rounded corners.
/// </summary>
internal sealed class ModifierButtonVisual : MonoBehaviour,
    IPointerEnterHandler,
    IPointerExitHandler,
    IButtonHoverFadeTarget
{
    private const float FadeDurationSeconds = 0.14f;
    private const float HoverFadeDurationSeconds = 0.1f;
    private static readonly Vector4 SliceBordersUv = new Vector4(
        18f / 320f,
        18f / 110f,
        18f / 320f,
        18f / 110f);
    private static readonly Vector2 CornerUiSize = new Vector2(2f, 2f);

    private NineSlicePanelRenderer _idleBackground = null!;
    private NineSlicePanelRenderer _selectedBackground = null!;
    private NineSlicePanelRenderer _hoverBackground = null!;
    private Button _button = null!;
    private ButtonHoverFadeGroup _hoverGroup = null!;
    private float _currentSelection;
    private float _targetSelection;
    private float _hoverAmount;
    private MotionScope _motion = null!;

    internal void Initialize(
        Button button,
        Image icon,
        TMP_Text title,
        TMP_Text description,
        Sprite idleSprite,
        Sprite selectedSprite,
        Sprite hoverSprite,
        ButtonHoverFadeGroup hoverGroup,
        bool selected)
    {
        _button = button;
        _hoverGroup = hoverGroup;
        _hoverGroup.Register(this);
        _motion = MotionUtils.Motion.For(this);
        var rendererTemplate = button.targetGraphic as Image ??
                               button.GetComponentInChildren<Image>(true);
        RemoveNativeVisuals(button, icon, title, description);

        _idleBackground = CreateBackground(
            button.transform,
            "IdleBackground",
            idleSprite,
            rendererTemplate,
            0);
        _selectedBackground = CreateBackground(
            button.transform,
            "SelectedBackground",
            selectedSprite,
            rendererTemplate,
            1);
        _hoverBackground = CreateBackground(
            button.transform,
            "HoverBackground",
            hoverSprite,
            rendererTemplate,
            2);
        button.targetGraphic = CreateHitTarget(button);

        _currentSelection = selected ? 1f : 0f;
        _targetSelection = _currentSelection;
        _hoverAmount = 0f;
        ApplyFade();
        ApplyHover();
    }

    internal void SetSelected(bool selected)
    {
        _targetSelection = selected ? 1f : 0f;
        if (selected)
        {
            _hoverGroup.ReleaseHover(this);
        }
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        _hoverGroup.RequestHover(this);
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        _hoverGroup.ReleaseHover(this);
    }

    bool IButtonHoverFadeTarget.HoverEnabled =>
        _button && _button.interactable && _targetSelection < 0.5f;

    void IButtonHoverFadeTarget.SetHovered(bool hovered)
    {
        AnimateHover(hovered ? 1f : 0f);
    }

    private void AnimateHover(float target)
    {
        if (Mathf.Approximately(_hoverAmount, target))
        {
            return;
        }

        _motion.Value(
            "hover",
            _hoverAmount,
            target,
            value =>
            {
                _hoverAmount = value;
                ApplyHover();
            },
            new MotionSpec(HoverFadeDurationSeconds));
    }

    private void Update()
    {
        if (Mathf.Approximately(_currentSelection, _targetSelection))
        {
            return;
        }

        _currentSelection = Mathf.MoveTowards(
            _currentSelection,
            _targetSelection,
            Time.unscaledDeltaTime / FadeDurationSeconds);
        ApplyFade();
    }

    private void ApplyFade()
    {
        _idleBackground.SetColor(new Color(
            1f,
            1f,
            1f,
            1f - _currentSelection));
        _selectedBackground.SetColor(new Color(
            1f,
            1f,
            1f,
            _currentSelection));
    }

    private void ApplyHover()
    {
        if (_hoverBackground)
        {
            _hoverBackground.SetColor(
                new Color(1f, 1f, 1f, _hoverAmount));
        }
    }

    private void OnDisable()
    {
        _motion?.Kill("hover");
        _hoverAmount = 0f;
        ApplyHover();
    }

    private void OnDestroy()
    {
        if (_hoverGroup)
        {
            _hoverGroup.Unregister(this);
        }
    }

    private static NineSlicePanelRenderer CreateBackground(
        Transform parent,
        string name,
        Sprite sprite,
        Image rendererTemplate,
        int siblingIndex)
    {
        var backgroundObject = new GameObject(name, typeof(RectTransform));
        backgroundObject.layer = rendererTemplate.gameObject.layer;
        var backgroundRect = (RectTransform)backgroundObject.transform;
        backgroundRect.SetParent(parent, false);
        backgroundRect.SetSiblingIndex(siblingIndex);
        backgroundRect.anchorMin = Vector2.zero;
        backgroundRect.anchorMax = Vector2.one;
        backgroundRect.offsetMin = Vector2.zero;
        backgroundRect.offsetMax = Vector2.zero;
        backgroundRect.localScale = Vector3.one;

        var renderer = backgroundObject.AddComponent<NineSlicePanelRenderer>();
        renderer.Initialize(
            sprite,
            rendererTemplate,
            Color.white,
            sliceBordersUv: SliceBordersUv,
            cornerUiSize: CornerUiSize);
        return renderer;
    }

    private static void RemoveNativeVisuals(
        Button button,
        Image icon,
        TMP_Text title,
        TMP_Text description)
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
            var keep = graphic == icon ||
                       graphic == title ||
                       graphic == description;
            graphic.enabled = keep;
            graphic.raycastTarget = false;
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
