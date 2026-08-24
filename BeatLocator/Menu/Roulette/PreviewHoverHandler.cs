using System;
using UnityEngine;
using UnityEngine.EventSystems;

namespace BeatLocator.Menu;

internal sealed class PreviewHoverHandler : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    internal Action<bool>? HoverChanged { get; set; }

    public void OnPointerEnter(PointerEventData eventData)
    {
        HoverChanged?.Invoke(true);
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        HoverChanged?.Invoke(false);
    }

    private void OnDisable()
    {
        HoverChanged?.Invoke(false);
    }
}
