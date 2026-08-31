using MotionUtils;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace BeatLocator.Menu;

/// <summary>
/// Fades a non-interactive artwork layer over an existing button without
/// changing layout geometry or the button's selected state.
/// </summary>
internal sealed class ButtonHoverFadeVisual : MonoBehaviour,
    IPointerEnterHandler,
    IPointerExitHandler,
    IButtonHoverFadeTarget
{
    private const float FadeDurationSeconds = 0.1f;

    private Button _button = null!;
    private ButtonHoverFadeGroup _group = null!;
    private Image _hoverArtwork = null!;
    private MotionScope _motion = null!;
    private float _hoverAmount;
    private bool _hoverEnabled = true;

    internal void Initialize(
        Button button,
        Sprite hoverSprite,
        Image rendererTemplate,
        ButtonHoverFadeGroup group,
        int siblingIndex = -1)
    {
        _button = button;
        _group = group;
        _group.Register(this);
        _motion = MotionUtils.Motion.For(this);

        var hoverObject = new GameObject(
            "HoverArtwork",
            typeof(RectTransform),
            typeof(CanvasRenderer));
        hoverObject.layer = rendererTemplate.gameObject.layer;
        var hoverRect = (RectTransform)hoverObject.transform;
        hoverRect.SetParent(button.transform, false);
        if (siblingIndex >= 0)
        {
            hoverRect.SetSiblingIndex(siblingIndex);
        }
        else
        {
            hoverRect.SetAsLastSibling();
        }

        hoverRect.anchorMin = Vector2.zero;
        hoverRect.anchorMax = Vector2.one;
        hoverRect.offsetMin = Vector2.zero;
        hoverRect.offsetMax = Vector2.zero;
        hoverRect.localScale = Vector3.one;

        _hoverArtwork = (Image)hoverObject.AddComponent(
            rendererTemplate.GetType());
        _hoverArtwork.sprite = hoverSprite;
        _hoverArtwork.type = Image.Type.Simple;
        _hoverArtwork.preserveAspect = false;
        _hoverArtwork.material = rendererTemplate.material;
        _hoverArtwork.color = Color.white;
        _hoverArtwork.raycastTarget = false;
        SetHoverAmount(0f);
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        _group.RequestHover(this);
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        _group.ReleaseHover(this);
    }

    bool IButtonHoverFadeTarget.HoverEnabled =>
        _hoverEnabled && _button && _button.interactable;

    void IButtonHoverFadeTarget.SetHovered(bool hovered)
    {
        AnimateTo(hovered ? 1f : 0f);
    }

    internal void SetHoverEnabled(bool enabled)
    {
        _hoverEnabled = enabled;
        if (!enabled)
        {
            AnimateTo(0f);
        }
    }

    private void AnimateTo(float target)
    {
        if (!_hoverArtwork || Mathf.Approximately(_hoverAmount, target))
        {
            return;
        }

        _motion.Value(
            "hover",
            _hoverAmount,
            target,
            SetHoverAmount,
            new MotionSpec(FadeDurationSeconds));
    }

    private void SetHoverAmount(float amount)
    {
        _hoverAmount = amount;
        if (_hoverArtwork)
        {
            _hoverArtwork.canvasRenderer.SetColor(
                new Color(1f, 1f, 1f, amount));
        }
    }

    private void OnDisable()
    {
        _motion?.Kill("hover");
        SetHoverAmount(0f);
    }

    private void OnDestroy()
    {
        if (_group)
        {
            _group.Unregister(this);
        }
    }
}
