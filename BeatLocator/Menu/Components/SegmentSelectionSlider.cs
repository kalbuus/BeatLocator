using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BeatLocator.Menu;

internal sealed class SegmentSelectionSlider : MonoBehaviour
{
    private const float HorizontalPadding = 3.5f;
    private const float VerticalPadding = 1.2f;
    private const float AnimationSmoothTime = 0.16f;
    private static readonly Color PanelTint =
        new Color(1f, 1f, 1f, 1f);
    private static readonly Color NormalTextTint =
        new Color(0.62f, 0.62f, 0.62f, 1f);
    private static readonly Color SelectedTextTint = Color.white;

    private RectTransform _track = null!;
    private RectTransform _slider = null!;
    private TMP_Text[] _labels = Array.Empty<TMP_Text>();
    private int _optionCount;
    private int _selectedIndex;
    private Vector2 _positionVelocity;
    private Vector2 _sizeVelocity;
    private bool _labelsSorted;
    private bool _positionInitialized;

    internal void Initialize(
        RectTransform track,
        Image sliderImage,
        int optionCount,
        int selectedIndex)
    {
        _track = track;
        _slider = sliderImage.rectTransform;
        _optionCount = Mathf.Max(1, optionCount);
        _selectedIndex = Mathf.Clamp(selectedIndex, 0, _optionCount - 1);
        _positionVelocity = Vector2.zero;
        _sizeVelocity = Vector2.zero;
        _labelsSorted = false;
        _positionInitialized = false;
        _labels = Array.Empty<TMP_Text>();

        _slider.SetParent(_track, false);
        _slider.SetAsFirstSibling();
        _slider.anchorMin = _track.pivot;
        _slider.anchorMax = _track.pivot;
        _slider.pivot = new Vector2(0.5f, 0.5f);
        _slider.localScale = Vector3.one;
        var panelRenderer = gameObject
            .AddComponent<NineSlicePanelRenderer>();
        panelRenderer.Initialize(
            sliderImage.sprite,
            sliderImage,
            PanelTint,
            removeExistingChildren: true);
        sliderImage.enabled = false;
        sliderImage.raycastTarget = false;
    }

    internal void MoveTo(int selectedIndex)
    {
        _selectedIndex = Mathf.Clamp(selectedIndex, 0, _optionCount - 1);
    }

    private void LateUpdate()
    {
        if (!_track ||
            !_slider ||
            !_track.gameObject.activeInHierarchy)
        {
            return;
        }

        if (!TryPrepareLabels())
        {
            // BSML can briefly remove all segment labels while rebuilding the
            // control. Do not leave the panel at its prefab/default size.
            _slider.sizeDelta = Vector2.zero;
            return;
        }

        ApplyTextTints();

        var selectedLabel = _labels[_selectedIndex];
        if (!selectedLabel)
        {
            InvalidateLabels();
            return;
        }

        selectedLabel.ForceMeshUpdate();
        var textMinimum = _track.InverseTransformPoint(
            selectedLabel.rectTransform.TransformPoint(
                selectedLabel.textBounds.min));
        var textMaximum = _track.InverseTransformPoint(
            selectedLabel.rectTransform.TransformPoint(
                selectedLabel.textBounds.max));
        var textCenter = (textMinimum + textMaximum) * 0.5f;
        var renderedTextSize = new Vector2(
            Mathf.Abs(textMaximum.x - textMinimum.x),
            Mathf.Abs(textMaximum.y - textMinimum.y));
        var segmentWidth = _track.rect.width / _optionCount;
        var targetPosition = new Vector2(
            textCenter.x,
            textCenter.y);
        var targetSize = new Vector2(
            Mathf.Min(
                renderedTextSize.x + HorizontalPadding,
                segmentWidth),
            renderedTextSize.y + VerticalPadding);

        if (_positionInitialized)
        {
            _slider.anchoredPosition = Vector2.SmoothDamp(
                _slider.anchoredPosition,
                targetPosition,
                ref _positionVelocity,
                AnimationSmoothTime,
                Mathf.Infinity,
                Time.unscaledDeltaTime);
            _slider.sizeDelta = Vector2.SmoothDamp(
                _slider.sizeDelta,
                targetSize,
                ref _sizeVelocity,
                AnimationSmoothTime,
                Mathf.Infinity,
                Time.unscaledDeltaTime);
        }
        else
        {
            _slider.anchoredPosition = targetPosition;
            _slider.sizeDelta = targetSize;
        }

        _positionInitialized = true;
    }

    private bool TryPrepareLabels()
    {
        if (!LabelsNeedRefresh())
        {
            return _selectedIndex < _labels.Length;
        }

        var liveLabels = new List<TMP_Text>();
        foreach (var label in _track.GetComponentsInChildren<TMP_Text>(true))
        {
            if (label && label.transform.IsChildOf(_track))
            {
                liveLabels.Add(label);
            }
        }

        liveLabels.Sort(CompareLabelsByPosition);
        _labels = liveLabels.ToArray();
        _labelsSorted = _labels.Length >= _optionCount;
        _positionInitialized = false;
        _positionVelocity = Vector2.zero;
        _sizeVelocity = Vector2.zero;

        return _labelsSorted && _selectedIndex < _labels.Length;
    }

    private bool LabelsNeedRefresh()
    {
        if (!_labelsSorted || _labels.Length < _optionCount)
        {
            return true;
        }

        foreach (var label in _labels)
        {
            if (!label || !label.transform.IsChildOf(_track))
            {
                return true;
            }
        }

        return false;
    }

    private static int CompareLabelsByPosition(
        TMP_Text? left,
        TMP_Text? right)
    {
        if (!left)
        {
            return !right ? 0 : 1;
        }

        if (!right)
        {
            return -1;
        }

        return left!.rectTransform.position.x.CompareTo(
            right!.rectTransform.position.x);
    }

    private void OnDisable()
    {
        InvalidateLabels();
    }

    private void InvalidateLabels()
    {
        _labels = Array.Empty<TMP_Text>();
        _labelsSorted = false;
        _positionInitialized = false;
        _positionVelocity = Vector2.zero;
        _sizeVelocity = Vector2.zero;
    }

    private void ApplyTextTints()
    {
        var labelCount = Mathf.Min(_optionCount, _labels.Length);
        for (var index = 0; index < labelCount; index++)
        {
            var label = _labels[index];
            if (label)
            {
                label.color = index == _selectedIndex
                    ? SelectedTextTint
                    : NormalTextTint;
            }
        }
    }

}
