using System;
using UnityEngine;
using UnityEngine.UI;

namespace BeatLocator.Menu;

internal sealed class NineSlicePanelRenderer : MonoBehaviour
{
    private const float SliceBorderUv = 0.12f;
    private const float SliceCenterUv = 0.76f;
    private const float SliceCornerUiSize = 1.2f;
    private const float SliceCenterSafetyMargin = 0.04f;

    private RectTransform _root = null!;
    private RectTransform[] _sliceRects = Array.Empty<RectTransform>();
    private Image[] _sliceImages = Array.Empty<Image>();
    private Color _color;
    private Vector2 _lastPanelSize;
    private Vector2 _lastLossyScale;
    private bool _layoutInitialized;

    internal void Initialize(
        Sprite source,
        Image rendererTemplate,
        Color color,
        bool removeExistingChildren = false)
    {
        _root = (RectTransform)transform;
        if (removeExistingChildren)
        {
            RemoveExistingChildren();
        }

        CreateSlices(source, rendererTemplate, color);
        RefreshLayout();
    }

    internal void SetColor(Color color)
    {
        _color = color;
        foreach (var sliceImage in _sliceImages)
        {
            if (sliceImage)
            {
                sliceImage.canvasRenderer.SetColor(color);
            }
        }
    }

    internal void Refresh()
    {
        RefreshLayout();
        foreach (var sliceImage in _sliceImages)
        {
            if (!sliceImage) continue;

            sliceImage.SetAllDirty();
            sliceImage.canvasRenderer.SetColor(_color);
        }
    }

    private void LateUpdate()
    {
        if (!_root || _sliceRects.Length != 9) return;

        var panelSize = _root.rect.size;
        var lossyScale = (Vector2)_root.lossyScale;
        if (!_layoutInitialized ||
            !Approximately(panelSize, _lastPanelSize) ||
            !Approximately(lossyScale, _lastLossyScale))
        {
            RefreshLayout();
        }
    }

    private void RefreshLayout()
    {
        if (!_root || _sliceRects.Length != 9) return;

        _lastPanelSize = _root.rect.size;
        _lastLossyScale = (Vector2)_root.lossyScale;
        UpdateLayout(_lastPanelSize);
        _layoutInitialized = true;
    }

    private static bool Approximately(Vector2 left, Vector2 right)
    {
        return Mathf.Approximately(left.x, right.x) &&
               Mathf.Approximately(left.y, right.y);
    }

    private void CreateSlices(
        Sprite source,
        Image rendererTemplate,
        Color color)
    {
        _color = color;
        var farEdge = 1f - SliceBorderUv;
        var sliceUvs = new[]
        {
            new Rect(0f, 0f, SliceBorderUv, SliceBorderUv),
            new Rect(SliceBorderUv, 0f, SliceCenterUv, SliceBorderUv),
            new Rect(farEdge, 0f, SliceBorderUv, SliceBorderUv),
            new Rect(0f, SliceBorderUv, SliceBorderUv, SliceCenterUv),
            new Rect(
                SliceBorderUv,
                SliceBorderUv,
                SliceCenterUv,
                SliceCenterUv),
            new Rect(
                farEdge,
                SliceBorderUv,
                SliceBorderUv,
                SliceCenterUv),
            new Rect(0f, farEdge, SliceBorderUv, SliceBorderUv),
            new Rect(
                SliceBorderUv,
                farEdge,
                SliceCenterUv,
                SliceBorderUv),
            new Rect(farEdge, farEdge, SliceBorderUv, SliceBorderUv)
        };

        _sliceRects = new RectTransform[sliceUvs.Length];
        _sliceImages = new Image[sliceUvs.Length];
        for (var index = 0; index < sliceUvs.Length; index++)
        {
            var sliceObject = new GameObject(
                $"NineSlice-{index}",
                typeof(RectTransform),
                typeof(CanvasRenderer));
            sliceObject.layer = rendererTemplate.gameObject.layer;

            var sliceRect = (RectTransform)sliceObject.transform;
            sliceRect.SetParent(_root, false);
            sliceRect.localScale = Vector3.one;

            var sliceImage = (Image)sliceObject.AddComponent(
                rendererTemplate.GetType());
            sliceImage.sprite = CreateSliceSprite(source, sliceUvs[index]);
            sliceImage.type = Image.Type.Simple;
            sliceImage.preserveAspect = false;
            sliceImage.material = rendererTemplate.material;
            sliceImage.color = Color.white;
            sliceImage.canvasRenderer.SetColor(color);
            sliceImage.raycastTarget = false;

            _sliceRects[index] = sliceRect;
            _sliceImages[index] = sliceImage;
        }
    }

    private void RemoveExistingChildren()
    {
        for (var index = _root.childCount - 1; index >= 0; index--)
        {
            var child = _root.GetChild(index);
            child.gameObject.SetActive(false);
            Destroy(child.gameObject);
        }
    }

    private static Sprite CreateSliceSprite(
        Sprite source,
        Rect normalizedRect)
    {
        var sourceRect = source.textureRect;
        var sliceRect = new Rect(
            sourceRect.x + sourceRect.width * normalizedRect.x,
            sourceRect.y + sourceRect.height * normalizedRect.y,
            sourceRect.width * normalizedRect.width,
            sourceRect.height * normalizedRect.height);

        return Sprite.Create(
            source.texture,
            sliceRect,
            new Vector2(0.5f, 0.5f),
            source.pixelsPerUnit,
            0,
            SpriteMeshType.FullRect);
    }

    private void UpdateLayout(Vector2 panelSize)
    {
        var scaleX = Mathf.Max(0.0001f, Mathf.Abs(_root.lossyScale.x));
        var scaleY = Mathf.Max(0.0001f, Mathf.Abs(_root.lossyScale.y));
        var minimumScale = Mathf.Min(scaleX, scaleY);
        var minimumPanelWorldSide = Mathf.Min(
            Mathf.Abs(panelSize.x) * scaleX,
            Mathf.Abs(panelSize.y) * scaleY);
        var cornerWorldSize = Mathf.Min(
            SliceCornerUiSize * minimumScale,
            Mathf.Max(
                0.0001f,
                (minimumPanelWorldSide -
                 SliceCenterSafetyMargin * minimumScale) * 0.5f));
        var cornerWidth = cornerWorldSize / scaleX;
        var cornerHeight = cornerWorldSize / scaleY;

        SetSliceRect(
            _sliceRects[0], Vector2.zero, Vector2.zero,
            Vector2.zero, new Vector2(cornerWidth, cornerHeight));
        SetSliceRect(
            _sliceRects[1], Vector2.zero, new Vector2(1f, 0f),
            new Vector2(cornerWidth, 0f),
            new Vector2(-cornerWidth, cornerHeight));
        SetSliceRect(
            _sliceRects[2], new Vector2(1f, 0f), new Vector2(1f, 0f),
            new Vector2(-cornerWidth, 0f),
            new Vector2(0f, cornerHeight));
        SetSliceRect(
            _sliceRects[3], Vector2.zero, new Vector2(0f, 1f),
            new Vector2(0f, cornerHeight),
            new Vector2(cornerWidth, -cornerHeight));
        SetSliceRect(
            _sliceRects[4], Vector2.zero, Vector2.one,
            new Vector2(cornerWidth, cornerHeight),
            new Vector2(-cornerWidth, -cornerHeight));
        SetSliceRect(
            _sliceRects[5], new Vector2(1f, 0f), Vector2.one,
            new Vector2(-cornerWidth, cornerHeight),
            new Vector2(0f, -cornerHeight));
        SetSliceRect(
            _sliceRects[6], new Vector2(0f, 1f), new Vector2(0f, 1f),
            new Vector2(0f, -cornerHeight),
            new Vector2(cornerWidth, 0f));
        SetSliceRect(
            _sliceRects[7], new Vector2(0f, 1f), Vector2.one,
            new Vector2(cornerWidth, -cornerHeight),
            new Vector2(-cornerWidth, 0f));
        SetSliceRect(
            _sliceRects[8], Vector2.one, Vector2.one,
            new Vector2(-cornerWidth, -cornerHeight), Vector2.zero);
    }

    private static void SetSliceRect(
        RectTransform rectTransform,
        Vector2 anchorMin,
        Vector2 anchorMax,
        Vector2 offsetMin,
        Vector2 offsetMax)
    {
        rectTransform.anchorMin = anchorMin;
        rectTransform.anchorMax = anchorMax;
        rectTransform.offsetMin = offsetMin;
        rectTransform.offsetMax = offsetMax;
    }
}
