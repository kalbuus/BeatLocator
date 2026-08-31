using System;
using UnityEngine;
using UnityEngine.UI;

namespace BeatLocator.Menu;

internal sealed class NineSlicePanelRenderer : MonoBehaviour
{
    private const float SliceBorderUv = 0.12f;
    private const float SliceCornerUiSize = 1.2f;
    private const float SliceCenterSafetyMargin = 0.04f;
    private static Sprite? _cachedSliceSource;
    private static Vector4 _cachedSliceBorders;
    private static Sprite[] _cachedSliceSprites = Array.Empty<Sprite>();

    private RectTransform _root = null!;
    private RectTransform[] _sliceRects = Array.Empty<RectTransform>();
    private Image[] _sliceImages = Array.Empty<Image>();
    private Color _color;
    private Vector2 _cornerUiSize =
        new Vector2(SliceCornerUiSize, SliceCornerUiSize);
    private Vector2 _lastPanelSize;
    private Vector2 _lastLossyScale;
    private bool _layoutInitialized;

    internal void Initialize(
        Sprite source,
        Image rendererTemplate,
        Color color,
        bool removeExistingChildren = false,
        Vector4? sliceBordersUv = null,
        Vector2? cornerUiSize = null)
    {
        _root = (RectTransform)transform;
        _cornerUiSize = cornerUiSize ??
                        new Vector2(
                            SliceCornerUiSize,
                            SliceCornerUiSize);
        if (removeExistingChildren)
        {
            RemoveExistingChildren();
        }

        CreateSlices(
            source,
            rendererTemplate,
            color,
            sliceBordersUv ?? new Vector4(
                SliceBorderUv,
                SliceBorderUv,
                SliceBorderUv,
                SliceBorderUv));
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
        Color color,
        Vector4 bordersUv)
    {
        _color = color;
        var left = bordersUv.x;
        var bottom = bordersUv.y;
        var right = bordersUv.z;
        var top = bordersUv.w;
        var centerWidth = 1f - left - right;
        var centerHeight = 1f - bottom - top;
        var rightEdge = 1f - right;
        var topEdge = 1f - top;
        var sliceUvs = new[]
        {
            new Rect(0f, 0f, left, bottom),
            new Rect(left, 0f, centerWidth, bottom),
            new Rect(rightEdge, 0f, right, bottom),
            new Rect(0f, bottom, left, centerHeight),
            new Rect(
                left,
                bottom,
                centerWidth,
                centerHeight),
            new Rect(
                rightEdge,
                bottom,
                right,
                centerHeight),
            new Rect(0f, topEdge, left, top),
            new Rect(
                left,
                topEdge,
                centerWidth,
                top),
            new Rect(rightEdge, topEdge, right, top)
        };
        var sliceSprites = GetSliceSprites(
            source,
            sliceUvs,
            bordersUv);

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
            sliceImage.sprite = sliceSprites[index];
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

    private static Sprite[] GetSliceSprites(
        Sprite source,
        Rect[] sliceUvs,
        Vector4 bordersUv)
    {
        if (_cachedSliceSource == source &&
            _cachedSliceBorders == bordersUv &&
            _cachedSliceSprites.Length == sliceUvs.Length)
        {
            return _cachedSliceSprites;
        }

        var sprites = new Sprite[sliceUvs.Length];
        for (var index = 0; index < sliceUvs.Length; index++)
        {
            sprites[index] = CreateSliceSprite(source, sliceUvs[index]);
        }

        _cachedSliceSource = source;
        _cachedSliceBorders = bordersUv;
        _cachedSliceSprites = sprites;
        return sprites;
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
        var sourceXMin = Mathf.Clamp(
            Mathf.CeilToInt(sourceRect.xMin),
            0,
            source.texture.width - 1);
        var sourceYMin = Mathf.Clamp(
            Mathf.CeilToInt(sourceRect.yMin),
            0,
            source.texture.height - 1);
        var sourceXMax = Mathf.Clamp(
            Mathf.FloorToInt(sourceRect.xMax),
            sourceXMin + 1,
            source.texture.width);
        var sourceYMax = Mathf.Clamp(
            Mathf.FloorToInt(sourceRect.yMax),
            sourceYMin + 1,
            source.texture.height);

        var xMin = Mathf.Clamp(
            Mathf.RoundToInt(
                sourceRect.x + sourceRect.width * normalizedRect.xMin),
            sourceXMin,
            sourceXMax - 1);
        var yMin = Mathf.Clamp(
            Mathf.RoundToInt(
                sourceRect.y + sourceRect.height * normalizedRect.yMin),
            sourceYMin,
            sourceYMax - 1);
        var xMax = Mathf.Clamp(
            Mathf.RoundToInt(
                sourceRect.x + sourceRect.width * normalizedRect.xMax),
            xMin + 1,
            sourceXMax);
        var yMax = Mathf.Clamp(
            Mathf.RoundToInt(
                sourceRect.y + sourceRect.height * normalizedRect.yMax),
            yMin + 1,
            sourceYMax);
        var sliceRect = Rect.MinMaxRect(xMin, yMin, xMax, yMax);

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
        var requestedCornerWorldSize = Mathf.Min(
            _cornerUiSize.x * scaleX,
            _cornerUiSize.y * scaleY);
        var cornerWorldSize = Mathf.Min(
            requestedCornerWorldSize,
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
