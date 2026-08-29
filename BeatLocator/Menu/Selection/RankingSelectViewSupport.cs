using System;
using System.Collections.Generic;
using System.IO;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BeatLocator.Menu;

/// <summary>
/// Small shared building blocks for ranking-provider search screens. Provider
/// layouts and actions remain in their own view controllers.
/// </summary>
internal static class RankingSelectViewSupport
{
    internal const float StarBuffer = 0.5f;
    internal const int MapCount = 60;
    internal const string NineSliceResource = "BeatLocator.Assets.9slice_bg.png";
    internal const string ExitIconResource = "BeatLocator.Assets.x_mark.png";
    internal const string CrossButtonResource =
        "BeatLocator.Assets.cross_button.png";
    internal const string SelectedButtonResource =
        "BeatLocator.Assets.button_bg_selected.png";
    internal const string UnselectedButtonResource =
        "BeatLocator.Assets.button_bg_not_selected.png";
    internal const string SelectionRowBackgroundResource =
        "BeatLocator.Assets.bg_duration.png";
    private static readonly Dictionary<string, Sprite> SpriteCache =
        new Dictionary<string, Sprite>(StringComparer.Ordinal);

    internal static List<string> CreateDifficulties()
    {
        return new List<string>
        {
            "Super Easy",
            "Easy",
            "Okay",
            "A Bit Hard",
            "E N D  M E"
        };
    }

    internal static List<string> CreateDurations()
    {
        return new List<string>
        {
            "ANY",
            "SHORT",
            "SMALL",
            "NORMAL",
            "LONG"
        };
    }

    internal static int NormalizeSelection(int selection, int optionCount)
    {
        return Mathf.Clamp(selection, 0, Mathf.Max(0, optionCount - 1));
    }

    internal static ToggleButtonVisual InitializeToggleVisual(
        Button button,
        TMP_Text label,
        bool active,
        Sprite nineSliceSprite,
        Image colorReference)
    {
        var visual = button.gameObject.AddComponent<ToggleButtonVisual>();
        visual.Initialize(
            button,
            label,
            nineSliceSprite,
            colorReference,
            active);
        return visual;
    }

    internal static void ForceLayout(RectTransform viewRoot)
    {
        if (!viewRoot) return;

        Canvas.ForceUpdateCanvases();
        LayoutRebuilder.ForceRebuildLayoutImmediate(viewRoot);
        RefreshPanels(viewRoot);
        Canvas.ForceUpdateCanvases();
    }

    private static void RefreshPanels(RectTransform viewRoot)
    {
        foreach (var panel in
                 viewRoot.GetComponentsInChildren<NineSlicePanelRenderer>(true))
        {
            panel.Refresh();
        }
    }

    internal static Sprite LoadSprite(string resourceName)
    {
        if (SpriteCache.TryGetValue(resourceName, out var cachedSprite) &&
            cachedSprite)
        {
            return cachedSprite;
        }

        using var stream = typeof(RankingSelectViewSupport).Assembly
                               .GetManifestResourceStream(resourceName)
                           ?? throw new InvalidOperationException(
                               $"Embedded image '{resourceName}' was not found.");
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);

        var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        if (!texture.LoadImage(buffer.ToArray()))
        {
            UnityEngine.Object.Destroy(texture);
            throw new InvalidOperationException(
                $"Could not load image '{resourceName}'.");
        }

        var sprite = Sprite.Create(
            texture,
            new Rect(0, 0, texture.width, texture.height),
            new Vector2(0.5f, 0.5f),
            100f);
        SpriteCache[resourceName] = sprite;
        return sprite;
    }

    internal static Sprite LoadSlicedSprite(
        string resourceName,
        Vector4 borderPixels,
        float pixelsPerUnit,
        float alphaMultiplier = 1f)
    {
        var cacheKey = resourceName + "|sliced|" + alphaMultiplier;
        if (SpriteCache.TryGetValue(cacheKey, out var cachedSprite) &&
            cachedSprite)
        {
            return cachedSprite;
        }

        var source = LoadSprite(resourceName);
        var texture = source.texture;
        if (!Mathf.Approximately(alphaMultiplier, 1f))
        {
            var pixels = texture.GetPixels32();
            for (var index = 0; index < pixels.Length; index++)
            {
                pixels[index].a = (byte)Mathf.Clamp(
                    Mathf.RoundToInt(pixels[index].a * alphaMultiplier),
                    0,
                    byte.MaxValue);
            }

            var adjustedTexture = new Texture2D(
                texture.width,
                texture.height,
                TextureFormat.RGBA32,
                false);
            adjustedTexture.filterMode = texture.filterMode;
            adjustedTexture.wrapMode = texture.wrapMode;
            adjustedTexture.SetPixels32(pixels);
            adjustedTexture.Apply();
            texture = adjustedTexture;
        }

        var slicedSprite = Sprite.Create(
            texture,
            new Rect(0f, 0f, texture.width, texture.height),
            new Vector2(0.5f, 0.5f),
            pixelsPerUnit,
            0,
            SpriteMeshType.FullRect,
            borderPixels);
        SpriteCache[cacheKey] = slicedSprite;
        return slicedSprite;
    }
}
