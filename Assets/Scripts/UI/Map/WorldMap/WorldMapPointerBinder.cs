#nullable enable

using System;
using UnityEngine.UIElements;

namespace Kern.UI;

internal sealed class WorldMapPointerBinder(WorldMapPanel panel) : IDisposable
{
    private bool _bound;
    private EventCallback<PointerDownEvent>? _onDown;
    private EventCallback<PointerMoveEvent>? _onMove;
    private EventCallback<PointerUpEvent>? _onUp;
    private EventCallback<ClickEvent>? _onClick;

    public void Bind(
        EventCallback<PointerDownEvent> onDown,
        EventCallback<PointerMoveEvent> onMove,
        EventCallback<PointerUpEvent> onUp,
        EventCallback<ClickEvent> onClick)
    {
        Image? image = panel.Image;
        if (image == null || _bound)
        {
            return;
        }

        image.RegisterCallback(onDown);
        image.RegisterCallback(onMove);
        image.RegisterCallback(onUp);
        image.RegisterCallback(onClick);
        _onDown = onDown;
        _onMove = onMove;
        _onUp = onUp;
        _onClick = onClick;
        _bound = true;
    }

    public void Dispose()
    {
        if (_onDown == null || _onMove == null || _onUp == null || _onClick == null)
        {
            return;
        }

        Dispose(_onDown, _onMove, _onUp, _onClick);
    }

    public void Dispose(
        EventCallback<PointerDownEvent> onDown,
        EventCallback<PointerMoveEvent> onMove,
        EventCallback<PointerUpEvent> onUp,
        EventCallback<ClickEvent> onClick)
    {
        if (!_bound || panel.Image == null)
        {
            return;
        }

        panel.Image.UnregisterCallback(onDown);
        panel.Image.UnregisterCallback(onMove);
        panel.Image.UnregisterCallback(onUp);
        panel.Image.UnregisterCallback(onClick);
        _bound = false;
        _onDown = null;
        _onMove = null;
        _onUp = null;
        _onClick = null;
    }
}
