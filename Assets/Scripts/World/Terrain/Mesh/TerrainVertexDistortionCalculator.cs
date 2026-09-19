#nullable enable

using MinesServer.Data;
using MinesServer.Networking.Server.Packets.Connection;
using UnityEngine;

namespace Kern.World.Terrain;

public sealed class TerrainVertexDistortionCalculator
{
    public TerrainRingGrid<Vector3> GridVertexOffsets { get; } = new();

    public bool EnableDistortion { get; set; } = true;

    public void EnsureCapacity(int meshWidth, int meshHeight)
    {
        if (GridVertexOffsets.Width != meshWidth + 1 || GridVertexOffsets.Height != meshHeight + 1)
        {
            GridVertexOffsets.EnsureSize(meshWidth + 1, meshHeight + 1);
        }
    }

    public void PrecalculateFull(TerrainCellCache cellCache, int meshWidth, int meshHeight, int worldWidth, int worldHeight)
    {
        EnsureCapacity(meshWidth, meshHeight);

        int gw = meshWidth + 1;
        int gh = meshHeight + 1;
        System.Threading.Tasks.Parallel.For(0, gw, x =>
        {
            for (int y = 0; y < gh; y++)
            {
                CalculateVertexNode(cellCache, x, y, worldWidth, worldHeight);
            }
        });
    }

    public void PrecalculateRegion(TerrainCellCache cellCache, int meshWidth, int meshHeight, int startX, int startY, int countX, int countY, int worldWidth, int worldHeight)
    {
        int gw = meshWidth + 1;
        int gh = meshHeight + 1;

        int vxMin = Mathf.Clamp(startX, 0, gw);
        int vxMax = Mathf.Clamp(startX + countX + 1, 0, gw);
        int vyMin = Mathf.Clamp(startY, 0, gh);
        int vyMax = Mathf.Clamp(startY + countY + 1, 0, gh);

        for (int x = vxMin; x < vxMax; x++)
        {
            for (int y = vyMin; y < vyMax; y++)
            {
                CalculateVertexNode(cellCache, x, y, worldWidth, worldHeight);
            }
        }
    }

    public void PrecalculateIncremental(TerrainCellCache cellCache, int meshWidth, int meshHeight, int dx, int dy, int worldWidth, int worldHeight)
    {
        EnsureCapacity(meshWidth, meshHeight);

        int gw = meshWidth + 1;
        int gh = meshHeight + 1;

        GridVertexOffsets.Scroll(dx, dy);

        int vxStart = 0;
        int vxLen = 0;
        int vyStart = 0;
        int vyLen = 0;

        if (dx > 0)
        {
            vxStart = Mathf.Max(0, gw - dx - 1);
            vxLen = gw - vxStart;
        }
        else if (dx < 0)
        {
            vxStart = 0;
            vxLen = Mathf.Min(gw, -dx + 1);
        }

        if (dy > 0)
        {
            vyStart = Mathf.Max(0, gh - dy - 1);
            vyLen = gh - vyStart;
        }
        else if (dy < 0)
        {
            vyStart = 0;
            vyLen = Mathf.Min(gh, -dy + 1);
        }

        if (vxLen > 0 || vyLen > 0)
        {
            if (vxLen > 0)
            {
                for (int x = vxStart; x < vxStart + vxLen; x++)
                {
                    for (int y = 0; y < gh; y++)
                    {
                        CalculateVertexNode(cellCache, x, y, worldWidth, worldHeight);
                    }
                }
            }

            if (vyLen > 0 && vxLen < gw)
            {
                int xStart = 0;
                int xEnd = gw;

                if (vxLen > 0)
                {
                    if (dx > 0)
                    {
                        xEnd = vxStart;
                    }
                    else
                    {
                        xStart = vxLen;
                    }
                }

                if (xStart < xEnd)
                {
                    for (int y = vyStart; y < vyStart + vyLen; y++)
                    {
                        for (int x = xStart; x < xEnd; x++)
                        {
                            CalculateVertexNode(cellCache, x, y, worldWidth, worldHeight);
                        }
                    }
                }
            }
        }
    }

    public void CalculateVertexNode(TerrainCellCache cellCache, int x, int y, int worldWidth = int.MaxValue, int worldHeight = int.MaxValue)
    {
        if (!EnableDistortion)
        {
            GridVertexOffsets[x, y] = Vector3.zero;
            return;
        }

        int cx = x + 1;
        int cy = y + 1;
        CachedCellData tl = cellCache.GetCellData(x, cy);
        CachedCellData tr = cellCache.GetCellData(cx, cy);
        CachedCellData bl = cellCache.GetCellData(x, y);
        CachedCellData br = cellCache.GetCellData(cx, y);

        int worldX = cellCache.CacheMinX + x;
        int worldY = cellCache.CacheMinY + y;

        GridVertexOffsets[x, y] = ComputeOffset(tl, tr, bl, br, worldX, worldY, worldWidth, worldHeight);
    }

    public static Vector3 ComputeOffset(
        CachedCellData tl,
        CachedCellData tr,
        CachedCellData bl,
        CachedCellData br,
        int worldX,
        int worldY,
        int worldWidth = int.MaxValue,
        int worldHeight = int.MaxValue)
    {
        if (worldX <= 0 || worldX >= worldWidth || worldY <= 0 || worldY >= worldHeight)
        {
            return Vector3.zero;
        }

        float rx = RandXd(worldX, worldY) / 16f;
        float ry = RandYd(worldX, worldY) / 16f;

        if (IsCause(tl) && IsCause(tr) && IsCause(bl) && IsCause(br))
        {
            return Vector3.zero;
        }

        if (IsBlock(tl) || IsBlock(tr) || IsBlock(bl) || IsBlock(br))
        {
            return Vector3.zero;
        }

        if (worldY == 0 || (IsCause(tl) && IsCause(br)) || (IsCause(tr) && IsCause(bl)))
        {
            return Vector3.zero;
        }

        if (IsCause(tl) && IsCause(tr))
        {
            return new Vector3(0, -ry, 0);
        }

        if (IsCause(tl) && IsCause(bl))
        {
            return new Vector3(-rx, 0, 0);
        }

        if (IsCause(tr) && IsCause(br))
        {
            return new Vector3(rx, 0, 0);
        }

        if (IsCause(bl) && IsCause(br))
        {
            return new Vector3(0, ry, 0);
        }

        if (IsCause(tl))
        {
            return new Vector3(-rx, -ry, 0);
        }

        if (IsCause(tr))
        {
            return new Vector3(rx, -ry, 0);
        }

        if (IsCause(bl))
        {
            return new Vector3(-rx, ry, 0);
        }

        if (IsCause(br))
        {
            return new Vector3(rx, ry, 0);
        }

        return Vector3.zero;
    }

    public static bool IsCause(CachedCellData data)
    {
        return data.Distortion == CellDistortionType.Cause;
    }

    public static bool IsBlock(CachedCellData data)
    {
        return data.Distortion == CellDistortionType.Block;
    }

    public static float RandXd(int x, int y)
    {
        int num = (((5 * x) + (11 * y)) * ((13 * x) + (7 * y))) % 3221;
        return (num * num) % 7;
    }

    public static float RandYd(int x, int y)
    {
        int num = (((17 * x) + (19 * y)) * ((23 * x) + (37 * y))) % 3469;
        return (num * num) % 7;
    }
}
