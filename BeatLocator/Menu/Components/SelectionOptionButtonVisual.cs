using BeatSaberMarkupLanguage.Components;
using HMUI;
using MotionUtils;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BeatLocator.Menu;

/// <summary>
/// Cross-fades between the supplied idle and selected artwork without moving
/// or resizing the button.
/// </summary>
internal sealed class SelectionOptionButtonVisual : MonoBehaviour
{
    private const float FadeDurationSeconds = 0.14f;
    private static readonly Color IdleTextColor =
        new Color(0.60f, 0.67f, 0.75f, 1f);
    private static readonly Color SelectedTextColor = Color.white;

    private Image _idleBackground = null!;
    private Image _selectedBackground = null!;
    private ButtonHoverFadeVisual _hoverVisual = null!;
    private TMP_Text _label = null!;
    private float _currentSelection;
    private float _targetSelection;
    private MotionScope _motion = null!;

    internal void Initialize(
        Button button,
        TMP_Text label,
        Sprite idleSprite,
        Sprite selectedSprite,
        Sprite hoverSprite,
        ButtonHoverFadeGroup hoverGroup,
        bool selected,
        float horizontalVisualBleed = 0f)
    {
        _label = label;
        _motion = MotionUtils.Motion.For(this);
        var rendererTemplate = button.targetGraphic as Image ??
                               button.GetComponentInChildren<Image>(true);
        RemoveNativeVisuals(button, label);

        _idleBackground = CreateBackground(
            button.transform,
            "IdleBackground",
            idleSprite,
            0,
            rendererTemplate,
            horizontalVisualBleed);
        _selectedBackground = CreateBackground(
            button.transform,
            "SelectedBackground",
            selectedSprite,
            1,
            rendererTemplate,
            horizontalVisualBleed);
        _hoverVisual = button.gameObject
            .AddComponent<ButtonHoverFadeVisual>();
        _hoverVisual.Initialize(
            button,
            hoverSprite,
            rendererTemplate,
            hoverGroup,
            2,
            horizontalVisualBleed: horizontalVisualBleed);

        label.fontStyle |= FontStyles.Bold | FontStyles.Italic;
        label.fontSize = GetFontSize(label.text);
        label.enableWordWrapping = false;
        label.alignment = TextAlignmentOptions.Center;
        CenterLabel(label.rectTransform);
        button.targetGraphic = CreateHitTarget(button);

        _currentSelection = selected ? 1f : 0f;
        _targetSelection = _currentSelection;
        _hoverVisual.SetHoverEnabled(!selected);
        ApplyFade();
    }

    internal void SetSelected(bool selected)
    {
        _targetSelection = selected ? 1f : 0f;
        _hoverVisual.SetHoverEnabled(!selected);
        if (Mathf.Approximately(_currentSelection, _targetSelection))
        {
            return;
        }

        _motion.Value(
            "selection",
            _currentSelection,
            _targetSelection,
            value =>
            {
                _currentSelection = value;
                ApplyFade();
            },
            new MotionSpec(FadeDurationSeconds));
    }

    private void ApplyFade()
    {
        SetAlpha(_idleBackground, 1f - _currentSelection);
        SetAlpha(_selectedBackground, _currentSelection);
        _label.color = Color.Lerp(
            IdleTextColor,
            SelectedTextColor,
            _currentSelection);
    }

    private static void SetAlpha(Image image, float alpha)
    {
        image.canvasRenderer.SetColor(new Color(1f, 1f, 1f, alpha));
    }

    private static Image CreateBackground(
        Transform parent,
        string name,
        Sprite sprite,
        int siblingIndex,
        Image rendererTemplate,
        float horizontalVisualBleed)
    {
        var backgroundObject = new GameObject(
            name,
            typeof(RectTransform),
            typeof(CanvasRenderer));
        backgroundObject.layer = rendererTemplate.gameObject.layer;
        var backgroundRect =
            (RectTransform)backgroundObject.transform;
        backgroundRect.SetParent(parent, false);
        backgroundRect.SetSiblingIndex(siblingIndex);
        backgroundRect.anchorMin = Vector2.zero;
        backgroundRect.anchorMax = Vector2.one;
        backgroundRect.offsetMin = new Vector2(
            -horizontalVisualBleed,
            0f);
        backgroundRect.offsetMax = new Vector2(
            horizontalVisualBleed,
            0f);

        var image = (Image)backgroundObject.AddComponent(
            rendererTemplate.GetType());
        image.sprite = sprite;
        image.type = Image.Type.Simple;
        image.preserveAspect = false;
        image.material = rendererTemplate.material;
        image.color = Color.white;
        image.raycastTarget = false;
        return image;
    }

    private static float GetFontSize(string text)
    {
        if (text.Length >= 9)
        {
            return 2.35f;
        }

        return text.Length >= 8 ? 2.4f : 2.6f;
    }

    private static void CenterLabel(RectTransform labelRect)
    {
        labelRect.anchorMin = Vector2.zero;
        labelRect.anchorMax = Vector2.one;
        labelRect.pivot = new Vector2(0.5f, 0.5f);
        labelRect.offsetMin = new Vector2(0f, -0.15f);
        labelRect.offsetMax = new Vector2(0f, -0.15f);
        labelRect.localScale = Vector3.one;
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
        var hitTargetRect = (RectTransform)hitTargetObject.transform;
        hitTargetRect.SetParent(button.transform, false);
        hitTargetRect.SetAsLastSibling();
        hitTargetRect.anchorMin = Vector2.zero;
        hitTargetRect.anchorMax = Vector2.one;
        hitTargetRect.offsetMin = new Vector2(0f, -0.45f);
        hitTargetRect.offsetMax = new Vector2(0f, 0.45f);

        var hitTarget = hitTargetObject.GetComponent<Image>();
        hitTarget.color = Color.clear;
        hitTarget.raycastTarget = true;
        return hitTarget;
    }

}
