using System.Linq;
using UnityEngine;
using UnityEngine.UI;

namespace BeatLocator.Menu;

/// <summary>
/// Shares the available row width equally between the modifier buttons that
/// currently exist and are active. Direct-child additions and removals trigger
/// an immediate relayout without rebuilding the surrounding menu.
/// </summary>
internal sealed class AdaptiveModifierButtonLayout : MonoBehaviour
{
    private RectTransform _container = null!;
    private float _spacing;
    private float _buttonHeight;
    private bool _initialized;

    internal void Initialize(
        RectTransform container,
        float spacing,
        float buttonHeight)
    {
        _container = container;
        _spacing = spacing;
        _buttonHeight = buttonHeight;
        _initialized = true;
        Refresh();
    }

    internal void Refresh()
    {
        if (!_initialized || !_container)
        {
            return;
        }

        var buttons = _container.GetComponentsInChildren<Button>(true)
            .Where(button =>
                button.transform.parent == _container &&
                button.gameObject.activeSelf)
            .OrderBy(button => button.transform.GetSiblingIndex())
            .ToArray();
        if (buttons.Length == 0)
        {
            return;
        }

        var containerWidth = _container.rect.width;
        if (containerWidth <= 0f)
        {
            containerWidth = _container.sizeDelta.x;
        }

        var totalSpacing = _spacing * (buttons.Length - 1);
        var buttonWidth = Mathf.Max(
            0f,
            (containerWidth - totalSpacing) / buttons.Length);
        var firstCenter = -containerWidth * 0.5f + buttonWidth * 0.5f;

        for (var index = 0; index < buttons.Length; index++)
        {
            var buttonRect = (RectTransform)buttons[index].transform;
            var layoutElement = buttonRect.GetComponent<LayoutElement>() ??
                                buttonRect.gameObject.AddComponent<LayoutElement>();
            layoutElement.ignoreLayout = true;
            buttonRect.anchorMin = new Vector2(0.5f, 0.5f);
            buttonRect.anchorMax = new Vector2(0.5f, 0.5f);
            buttonRect.pivot = new Vector2(0.5f, 0.5f);
            buttonRect.sizeDelta = new Vector2(buttonWidth, _buttonHeight);
            buttonRect.anchoredPosition = new Vector2(
                firstCenter + index * (buttonWidth + _spacing),
                0f);
            buttonRect.localScale = Vector3.one;
        }
    }

    private void OnTransformChildrenChanged()
    {
        Refresh();
    }

    private void OnRectTransformDimensionsChange()
    {
        Refresh();
    }
}
