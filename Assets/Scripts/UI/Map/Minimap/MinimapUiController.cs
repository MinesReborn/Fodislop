#nullable enable

using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace Kern.UI;

internal sealed class MinimapUiController(
    UIDocument document,
    Texture2D texture,
    Action openMap) : IDisposable
{
    private MinimapView? _view;

    public bool IsCreated => _view != null;

    public bool TryCreate()
    {
        if (_view != null)
        {
            return true;
        }

        if (document == null || document.rootVisualElement == null)
        {
            // UI Toolkit can attach the panel after Start; the caller retries
            // creation from its lifecycle update.
            return false;
        }

        _view = MinimapView.Create(document, texture, openMap);
        return true;
    }

    public void UpdateCoordinates(int x, int y) => _view?.UpdateCoordinates(x, y);

    public void MarkDirty() => _view?.MarkDirty();

    public void SetVisible(bool visible) => _view?.SetVisible(visible);

    public void Dispose()
    {
        _view?.Dispose();
        _view = null;
    }
}
