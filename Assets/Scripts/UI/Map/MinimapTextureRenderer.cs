#nullable enable

using System.Collections.Generic;
using Kern.World;
using MinesServer.Data;
using UnityEngine;

namespace Kern.UI;

internal sealed class MinimapTextureRenderer
{
    private static readonly Color32 _UnloadedColor = new(0, 0, 0, 255);
    private static readonly Color32 _OutOfBoundsColor = new(0, 0, 0, 255);
    private static readonly Color32 _MarkerColor = Color.white;
    private static readonly Color32 _CenterColor = Color.red;

    private readonly Color32[] _cellColors = new Color32[256];
    private Color32[]? _pixelColors;
    private readonly int _uiSize;

    public MinimapTextureRenderer(int uiSize)
    {
        _uiSize = uiSize;
        _pixelColors = new Color32[uiSize * uiSize];
    }

    public void CacheCellColors(MapManager? mapManager)
    {
        if (mapManager == null)
        {
            return;
        }

        for (int i = 0; i <= 255; i++)
        {
            CellType cellType = (CellType)i;
            if (cellType == CellType.Unloaded)
            {
                _cellColors[i] = _UnloadedColor;
                continue;
            }

            Color color = mapManager.GetCellMinimapColor(cellType);
            if (color.a < 0.01f)
            {
                color = new Color(0.3f, 0.3f, 0.3f, 1f);
            }

            _cellColors[i] = (Color32)color;
        }
    }

    public bool Render(
        Texture2D? texture,
        int playerX,
        int playerY,
        int worldWidth,
        int worldHeight,
        MapCellSampler cellSampler)
    {
        int halfSize = _uiSize / 2;
        int minX = playerX - halfSize;
        int texSize = _uiSize;
        Color32[]? colors = _pixelColors;
        if (colors == null)
        {
            return false;
        }

        int index = 0;
        bool hasLoadedCells = false;

        for (int texY = 0; texY < texSize; texY++)
        {
            int serverY = playerY + halfSize - texY;

            if (serverY < 0 || serverY >= worldHeight)
            {
                int end = index + texSize;
                while (index < end)
                {
                    colors[index++] = _OutOfBoundsColor;
                }

                continue;
            }

            for (int texX = 0; texX < texSize; texX++)
            {
                int serverX = minX + texX;

                if (serverX < 0 || serverX >= worldWidth)
                {
                    colors[index++] = _OutOfBoundsColor;
                    continue;
                }

                if (cellSampler.TryGetCell(serverX, serverY, out CellType cellType))
                {
                    hasLoadedCells = true;
                    colors[index++] = cellType == CellType.Unloaded
                        ? _UnloadedColor
                        : _cellColors[(byte)cellType];
                }
                else
                {
                    colors[index++] = _UnloadedColor;
                }
            }
        }

        int cx = halfSize;
        colors[(cx * texSize) + cx - 1] = _MarkerColor;
        colors[(cx * texSize) + cx] = _CenterColor;
        colors[(cx * texSize) + cx + 1] = _MarkerColor;
        colors[((cx - 1) * texSize) + cx] = _MarkerColor;
        colors[((cx + 1) * texSize) + cx] = _MarkerColor;

        if (texture != null)
        {
            texture.SetPixelData(colors, 0);
            texture.Apply(updateMipmaps: false, makeNoLongerReadable: false);
        }

        return hasLoadedCells;
    }
}
