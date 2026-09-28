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

    public void Bind(
        EventCallback<PointerDownEvent> onDown,
        EventCallback<PointerMoveEvent> onMove,
        EventCallback<PointerUpEvent> onUp)
    {
        Image? image = panel.Image;
        if (image == null || _bound)
        {
            return;
        }

        image.RegisterCallback(onDown);
        image.RegisterCallback(onMove);
        image.RegisterCallback(onUp);
        _onDown = onDown;
        _onMove = onMove;
        _onUp = onUp;
        _bound = true;
    }

    public void Dispose()
    {
        if (_onDown == null || _onMove == null || _onUp == null)
        {
            return;
        }

        Dispose(_onDown, _onMove, _onUp);
    }

    public void Dispose(
        EventCallback<PointerDownEvent> onDown,
        EventCallback<PointerMoveEvent> onMove,
        EventCallback<PointerUpEvent> onUp)
    {
        if (!_bound || panel.Image == null)
        {
            return;
        }

        panel.Image.UnregisterCallback(onDown);
        panel.Image.UnregisterCallback(onMove);
        panel.Image.UnregisterCallback(onUp);
        _bound = false;
        _onDown = null;
        _onMove = null;
        _onUp = null;
    }
}
