#nullable enable

using System;
using System.Collections.Generic;
using Kern.World;
using Kern.Core.Interfaces;
using UnityEngine;

using Kern.AssetPipeline.Loading;
using Kern.UI.Screens;

namespace Kern.UI.Map;

internal sealed class MinimapTextureRenderer : IDisposable
{
    private static readonly Color32 s_pathColor = new(255, 214, 0, 255);
    private static readonly Color32 s_pathTargetColor = new(255, 255, 255, 255);

    private readonly MapViewportRenderer _viewportRenderer = new();
    private readonly int _uiSize;
    private readonly Color32[] _pathPixels;
    private int _drawnPathPixels;

    public MinimapTextureRenderer(int uiSize)
    {
        _uiSize = uiSize;
        _pathPixels = new Color32[uiSize * uiSize];
        PathOverlay = RuntimeTextureFactory.CreateRGBA32NoMip(
            uiSize,
            uiSize,
            "MinimapPathOverlay",
            RuntimeTextureColorSpace.Srgb,
            FilterMode.Point,
            TextureWrapMode.Clamp);
        PathOverlay.SetPixelData(_pathPixels, 0);
        PathOverlay.Apply(updateMipmaps: false, makeNoLongerReadable: false);
        DynamicAtlasConfigurator.RegisterRuntimeRedrawn(PathOverlay);
    }

    // Transparent texture over the minimap: the remaining click route.
    public Texture2D PathOverlay { get; }

    public void CacheCellColors(MapManager mapManager) =>
        _viewportRenderer.InitColorTable(mapManager);

    public Vector2Int PixelToServerCell(int pixelX, int pixelY, int playerX, int playerY) =>
        MapProjection.MinimapPixelToServerCell(pixelX, pixelY, playerX, playerY, _uiSize);

    public Vector2Int ServerCellToPixel(int serverX, int serverY, int playerX, int playerY) =>
        MapProjection.ServerCellToMinimapPixel(serverX, serverY, playerX, playerY, _uiSize);

    public bool TryRender(
        RenderTexture? texture,
        bool isPresentationStable,
        int playerX,
        int playerY,
        int worldWidth,
        int worldHeight,
        MapCellSampler cellSampler,
        IClickPathWalker? pathWalker,
        IReadOnlyList<Vector2Int>? fallbackPath,
        Action? markDirty,
        bool drawPlayerMarker = true)
    {
        if (!isPresentationStable)
        {
            return false;
        }

        IReadOnlyList<Vector2Int>? path = pathWalker?.Path ?? fallbackPath;
        int pathStartIndex = pathWalker?.PathIndex ?? 0;
        bool hasLoadedCells = Render(
            texture,
            playerX,
            playerY,
            worldWidth,
            worldHeight,
            cellSampler,
            drawPlayerMarker ? path : null,
            pathStartIndex);
        markDirty?.Invoke();
        return hasLoadedCells;
    }

    public bool Render(
        RenderTexture? texture,
        int playerX,
        int playerY,
        int worldWidth,
        int worldHeight,
        MapCellSampler cellSampler,
        IReadOnlyList<Vector2Int>? clickPath,
        int pathStartIndex)
    {
        bool loaded = _viewportRenderer.Render(
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
        DrawPath(clickPath, pathStartIndex, playerX, playerY);
        return loaded;
    }

    // Remaining route from the next step to the target: yellow cells, the
    // target white. Skips the texture upload while there is nothing to clear.
    private void DrawPath(IReadOnlyList<Vector2Int>? clickPath, int pathStartIndex, int playerX, int playerY)
    {
        int count = clickPath?.Count ?? 0;
        if (count == 0 && _drawnPathPixels == 0)
        {
            return;
        }

        Array.Clear(_pathPixels, 0, _pathPixels.Length);
        _drawnPathPixels = 0;
        for (int i = Math.Max(0, pathStartIndex); i < count; i++)
        {
            Vector2Int cell = clickPath![i];
            Vector2Int pixel = ServerCellToPixel(cell.x, cell.y, playerX, playerY);
            if (pixel.x < 0 || pixel.y < 0 || pixel.x >= _uiSize || pixel.y >= _uiSize)
            {
                continue;
            }

            _pathPixels[(pixel.y * _uiSize) + pixel.x] = i == count - 1 ? s_pathTargetColor : s_pathColor;
            _drawnPathPixels++;
        }

        PathOverlay.SetPixelData(_pathPixels, 0);
        PathOverlay.Apply(updateMipmaps: false, makeNoLongerReadable: false);
    }

    public void Dispose()
    {
        _viewportRenderer.Dispose();
        UnityEngine.Object.Destroy(PathOverlay);
    }
}
