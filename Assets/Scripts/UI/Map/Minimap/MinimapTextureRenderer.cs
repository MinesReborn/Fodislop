#nullable enable

using System;
using Kern.World;
using UnityEngine;

namespace Kern.UI;

internal sealed class MinimapTextureRenderer : IDisposable
{
    private readonly MapViewportRenderer _viewportRenderer = new();
    private readonly int _uiSize;

    public MinimapTextureRenderer(int uiSize)
    {
        _uiSize = uiSize;
    }

    public void CacheCellColors(MapManager mapManager) =>
        _viewportRenderer.InitColorTable(mapManager);

    public bool Render(
        RenderTexture? texture,
        int playerX,
        int playerY,
        int worldWidth,
        int worldHeight,
        MapCellSampler cellSampler,
        bool drawPlayerMarker = true)
    {
        return _viewportRenderer.Render(
            texture,
            worldWidth,
            worldHeight,
            cellSampler,
            null,
            _uiSize,
            _uiSize,
            1f,
            playerX,
            playerY);
    }

    public void Dispose()
    {
        _viewportRenderer.Dispose();
    }
}
