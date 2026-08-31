using System.Collections.Generic;
using UnityEngine;

namespace BeatLocator.Menu;

internal interface IButtonHoverFadeTarget
{
    bool HoverEnabled { get; }

    void SetHovered(bool hovered);
}

/// <summary>
/// Keeps hover exclusive across the screen. VR pointer transitions can omit a
/// matching PointerExit, so entering a new button explicitly clears every
/// previous hover target.
/// </summary>
internal sealed class ButtonHoverFadeGroup : MonoBehaviour
{
    private readonly List<IButtonHoverFadeTarget> _targets = new();

    internal void Register(IButtonHoverFadeTarget target)
    {
        if (!_targets.Contains(target))
        {
            _targets.Add(target);
        }
    }

    internal void Unregister(IButtonHoverFadeTarget target)
    {
        _targets.Remove(target);
    }

    internal void RequestHover(IButtonHoverFadeTarget target)
    {
        for (var index = _targets.Count - 1; index >= 0; index--)
        {
            var candidate = _targets[index];
            if (candidate is Object unityObject && !unityObject)
            {
                _targets.RemoveAt(index);
                continue;
            }

            candidate.SetHovered(
                ReferenceEquals(candidate, target) &&
                target.HoverEnabled);
        }
    }

    internal void ReleaseHover(IButtonHoverFadeTarget target)
    {
        target.SetHovered(false);
    }
}
