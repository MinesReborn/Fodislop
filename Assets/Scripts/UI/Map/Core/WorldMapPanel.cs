#nullable enable

using System;
using Kern.Core.Localization;
using UnityEngine;
using UnityEngine.UIElements;

namespace Kern.UI;

/// <summary>Owns WorldMap's UI Toolkit bindings and presentation-only panel state.</summary>
internal sealed class WorldMapPanel : IDisposable
{
    private UIDocument? _document;
    private VisualElement? _overlay;
    private Image? _image;
    private Button? _closeButton;
    private Button? _followButton;
    private Label? _status;
    private EventCallback<WheelEvent>? _wheelCallback;
    private Action _closeRequested = null!;
    private Action _followPlayer = null!;
    private bool _bindingFailureReported;

    public VisualElement? Overlay => _overlay;

    public Image? Image => _image;

    public bool IsBound => _overlay != null && _image != null;

    public bool IsDocumentDisabled => _document != null && !_document.enabled;

    public bool TryBind(
        UIDocument? document,
        Action closeRequested,
        Action followPlayer,
        EventCallback<WheelEvent> wheelCallback)
    {
        if (IsBound)
        {
            return true;
        }

        _document = document;
        if (_document == null)
        {
            ReportBindingFailure("World map panel cannot bind because its UIDocument was not injected.");
            return false;
        }

        if (_document.rootVisualElement.panel == null)
        {
            return false;
        }

        VisualElement? overlay = _document.rootVisualElement.Q<VisualElement>("WorldMapOverlay");
        if (overlay == null)
        {
            ReportBindingFailure("World map panel cannot bind because WorldMapOverlay is missing from its UIDocument.");
            return false;
        }

        Image? image = overlay.Q<Image>("WorldMapImage");
        Button? closeButton = overlay.Q<Button>("WorldMapCloseButton");
        Button? followButton = overlay.Q<Button>("WorldMapFollowPlayerButton");
        Label? status = overlay.Q<Label>("WorldMapStatus");
        if (image == null || closeButton == null || followButton == null || status == null)
        {
            ReportBindingFailure(
                "World map panel cannot bind because a required map control is missing.");
            return false;
        }

        _overlay = overlay;
        _image = image;
        _closeButton = closeButton;
        _followButton = followButton;
        _status = status;
        _image.image = null;
        _closeRequested = closeRequested;
        _followPlayer = followPlayer;
        _closeButton.clicked += _closeRequested;
        _followButton.clicked += _followPlayer;
        _wheelCallback = wheelCallback;
        _bindingFailureReported = false;
        _document.rootVisualElement.RegisterCallback(
            _wheelCallback,
            TrickleDown.TrickleDown);
        return true;
    }

    public void Show()
    {
        if (_overlay != null)
        {
            UIState.Show(_overlay);
        }
    }

    public void Hide()
    {
        if (_overlay != null)
        {
            UIState.Hide(_overlay);
        }
    }

    public void UpdatePreparationStatus(
        bool mipReady,
        float cellsPerPixel,
        int chunkSize,
        bool failed,
        int progress,
        int total,
        ILocalizationService localization)
    {
        if (_status == null)
        {
            return;
        }

        bool visible = !mipReady && cellsPerPixel >= chunkSize;
        _status.EnableInClassList("is-hidden", !visible);
        if (!visible)
        {
            return;
        }

        _status.text = failed
            ? localization.Get("hud.map_prepare_failed")
            : total > 0
                ? localization.Get("hud.map_preparing_progress", (int)(100f * progress / total))
                : localization.Get("hud.map_preparing");
    }

    public void Dispose()
    {
        if (_document?.rootVisualElement != null && _wheelCallback != null)
        {
            _document.rootVisualElement.UnregisterCallback(
                _wheelCallback,
                TrickleDown.TrickleDown);
        }

        if (_closeButton != null)
        {
            _closeButton.clicked -= _closeRequested;
        }

        if (_followButton != null)
        {
            _followButton.clicked -= _followPlayer;
        }
    }

    private void ReportBindingFailure(string message)
    {
        if (_bindingFailureReported)
        {
            return;
        }

        _bindingFailureReported = true;
        Debug.LogError(message);
    }
}
