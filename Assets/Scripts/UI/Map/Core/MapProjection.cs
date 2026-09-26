#nullable enable

using Kern.World;
using MinesServer.Data;
using UnityEngine;

namespace Kern.UI;

/// <summary>Shared server-cell projection and unknown-cell appearance for all map surfaces.</summary>
internal static class MapProjection
{
    public static Color32[] BuildCellColorTable(MapManager manager)
    {
        var colors = new Color32[256];
        for (int i = 0; i < colors.Length; i++)
        {
            CellType type = (CellType)i;
            colors[i] = (Color32)manager.GetCellMinimapColor(type);
        }

        return colors;
    }

    public static Vector2 ServerCellToTexturePixel(
        float serverX,
        float serverY,
        float centerX,
        float centerY,
        float cellsPerPixel,
        int width,
        int height)
    {
        return new Vector2(
            (serverX - centerX) / cellsPerPixel + width * 0.5f,
            height * 0.5f - 1f - (serverY - centerY) / cellsPerPixel);
    }

    public static Vector2 MapPixelToServer(
        float pixelX,
        float pixelY,
        float centerX,
        float centerY,
        float cellsPerPixel,
        int width,
        int height)
    {
        return new Vector2(
            MapPixelXToServer(pixelX, centerX, cellsPerPixel, width),
            MapPixelYToServer(pixelY, centerY, cellsPerPixel, height));
    }

    public static float MapPixelXToServer(float pixelX, float centerX, float cellsPerPixel, int width) =>
        centerX + (pixelX - width * 0.5f) * cellsPerPixel;

    public static float MapPixelYToServer(float pixelY, float centerY, float cellsPerPixel, int height) =>
        centerY + (pixelY - height * 0.5f) * cellsPerPixel;

    public static Vector2 MapPixelDeltaToServer(float pixelDeltaX, float pixelDeltaY, float cellsPerPixel)
    {
        return new Vector2(pixelDeltaX * cellsPerPixel, pixelDeltaY * cellsPerPixel);
    }

    public static Vector2Int MinimapPixelToServerCell(
        int pixelX,
        int pixelY,
        int centerX,
        int centerY,
        int size)
    {
        int halfSize = size / 2;
        return new Vector2Int(centerX - halfSize + pixelX, centerY + halfSize - pixelY);
    }

    public static Vector2Int ServerCellToMinimapPixel(
        int serverX,
        int serverY,
        int centerX,
        int centerY,
        int size)
    {
        int halfSize = size / 2;
        return new Vector2Int(serverX - centerX + halfSize, centerY + halfSize - serverY);
    }

    public static Color32 UnknownCellColor(int serverX, int serverY)
    {
        bool stripe = ((serverX + serverY) & 3) < 2;
        return stripe ? new Color32(48, 43, 66, 255) : new Color32(28, 27, 39, 255);
    }

    public static Color32 SampleCellColor(
        MapCellSampler sampler,
        Color32[] cellColors,
        int serverX,
        int serverY,
        int worldWidth,
        int worldHeight,
        Color32 outOfBoundsColor,
        out bool hasLoadedCell)
    {
        hasLoadedCell = false;
        if (serverX < 0 || serverX >= worldWidth || serverY < 0 || serverY >= worldHeight)
        {
            return outOfBoundsColor;
        }

        if (!sampler.TryGetCell(serverX, serverY, out CellType type) || type == CellType.Unloaded)
        {
            return UnknownCellColor(serverX, serverY);
        }

        hasLoadedCell = true;
        return cellColors[(byte)type];
    }
}
