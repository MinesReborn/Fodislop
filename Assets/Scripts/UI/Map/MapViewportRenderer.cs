#nullable enable

using System;
using Kern.Core;
using Kern.Core.Interfaces;
using Kern.World;
using MinesServer.Data;
using UnityEngine;

namespace Kern.UI;

internal sealed class MapViewportRenderer
{
    private static readonly Color32 _UnloadedColor = new(0, 0, 0, 255);
    private readonly Color32 _defaultColor = _UnloadedColor;
    private readonly Color32[] _cellColorTable = new Color32[256];
    private Color32[]? _pixelBuffer;

    public void InitColorTable(MapManager manager)
    {
        if (manager == null)
        {
            throw new InvalidOperationException("[MapViewportRenderer] Cannot build color table: map manager is not initialized");
        }

        for (int i = 0; i < 256; i++)
        {
            CellType type = (CellType)i;
            Color color = manager.GetCellMinimapColor(type);
            if (color.a < 0.01f)
            {
                color = new Color(0.3f, 0.3f, 0.3f, 1f);
            }

            _cellColorTable[i] = (Color32)color;
        }
    }

    public void Render(
        Texture2D? mapTexture,
        MapManager manager,
        MapCellSampler cellSampler,
        int texWidth,
        int texHeight,
        float cellsPerPixel,
        float viewCenterX,
        float viewCenterY,
        ILocalPlayer? player,
        bool playerBlinkState)
    {
        int worldW = manager.WorldWidth;
        int worldH = manager.WorldHeight;
        float cp = cellsPerPixel;
        float cx = viewCenterX;
        float cy = viewCenterY;
        int texW = texWidth;
        int texH = texHeight;

        Color32 defaultCol = _defaultColor;
        if (_pixelBuffer == null || _pixelBuffer.Length != texW * texH)
        {
            _pixelBuffer = new Color32[texW * texH];
        }

        for (int i = 0; i < _pixelBuffer.Length; i++)
        {
            _pixelBuffer[i] = defaultCol;
        }

        // Sample from screen pixels instead of iterating over every world
        // cell. When zoomed out, walking the whole world paints the same pixel many times.
        for (int py = 0; py < texH; py++)
        {
            int rowStart = py * texW;

            // Texture2D row zero is the bottom of the displayed map image.
            // Server coordinates use a top-left origin, so the bottom texture
            // row must sample the largest server Y in the viewport.
            float screenRowFromTop = (texH - 1 - py) + 0.5f;
            float worldY = cy + ((screenRowFromTop - (texH * 0.5f)) * cp);
            int serverY = Mathf.FloorToInt(worldY);

            for (int px = 0; px < texW; px++)
            {
                float worldX = cx + ((px + 0.5f - (texW * 0.5f)) * cp);
                int serverX = Mathf.FloorToInt(worldX);
                Color32 color = _defaultColor;

                if (serverX >= 0 && serverX < worldW && serverY >= 0 && serverY < worldH)
                {
                    CellType type = cellSampler.TryGetCell(serverX, serverY, out CellType sampled)
                        ? sampled
                        : CellType.Unloaded;

                    color = type == CellType.Unloaded
                        ? _UnloadedColor
                        : _cellColorTable[(byte)type];
                }

                _pixelBuffer[rowStart + px] = color;
            }
        }

        if (player != null && playerBlinkState)
        {
            Vector2Int playerPos = player.Position;

            float halfW = texW * 0.5f * cp;
            float halfH = texH * 0.5f * cp;
            float leftX = cx - halfW;
            float rightX = cx + halfW;
            float topServerY = cy - halfH;
            float bottomServerY = cy + halfH;

            if (playerPos.x + 1f >= leftX && playerPos.x <= rightX &&
                playerPos.y + 1f >= topServerY && playerPos.y <= bottomServerY)
            {
                float pixelX = ((playerPos.x - cx) / cp) + (texW * 0.5f);
                float pixelY = (texH * 0.5f) - 1f - ((playerPos.y - cy) / cp);
                float markerSize = Mathf.Max(1f, 1f / cp);

                int pxStart = Mathf.Clamp(Mathf.RoundToInt(pixelX), 0, texW - 1);
                int pxEnd = Mathf.Clamp(Mathf.RoundToInt(pixelX + markerSize), 0, texW - 1);
                int pyStart = Mathf.Clamp(Mathf.RoundToInt(pixelY), 0, texH - 1);
                int pyEnd = Mathf.Clamp(Mathf.RoundToInt(pixelY + markerSize), 0, texH - 1);

                Color32 playerColor = new Color32(255, 0, 0, 255);
                for (int py = pyStart; py <= pyEnd; py++)
                {
                    int rowStart = py * texW;
                    for (int px = pxStart; px <= pxEnd; px++)
                    {
                        _pixelBuffer[rowStart + px] = playerColor;
                    }
                }
            }
        }

        if (mapTexture != null)
        {
            mapTexture.SetPixelData(_pixelBuffer, 0);
            mapTexture.Apply(updateMipmaps: false, makeNoLongerReadable: false);
        }
    }
}
