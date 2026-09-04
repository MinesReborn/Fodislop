#nullable enable

using System;
using MinesServer.Data;
using MinesServer.Networking.Server.Packets.Connection;
using UnityEngine;

namespace Fodinae.World.Terrain
{
    public class TerrainPrecalculator
    {
        public Vector3[,] GridVertexOffsets { get; private set; } = null!;
        public int[,] CellTilingDescriptors { get; private set; } = null!;
        public int[,] CellCornerVariants { get; private set; } = null!;
        public byte[,] CellReliefMasks { get; private set; } = null!;
        public byte[,] CellSolidBoundaryMasks { get; private set; } = null!;
        public bool EnableDistortion { get; set; } = true;

        public void EnsureCapacity(int meshWidth, int meshHeight)
        {
            if (GridVertexOffsets == null || GridVertexOffsets.GetLength(0) != meshWidth + 1 || GridVertexOffsets.GetLength(1) != meshHeight + 1)
            {
                GridVertexOffsets = new Vector3[meshWidth + 1, meshHeight + 1];
                CellTilingDescriptors = new int[meshWidth, meshHeight];
                CellCornerVariants = new int[meshWidth, meshHeight];
                CellReliefMasks = new byte[meshWidth, meshHeight];
                CellSolidBoundaryMasks = new byte[meshWidth, meshHeight];
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

            System.Threading.Tasks.Parallel.For(0, meshWidth, x =>
            {
                for (int y = 0; y < meshHeight; y++)
                {
                    CalculateCellNode(cellCache, x, y);
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

            int cxMin = Mathf.Clamp(startX, 0, meshWidth);
            int cxMax = Mathf.Clamp(startX + countX, 0, meshWidth);
            int cyMin = Mathf.Clamp(startY, 0, meshHeight);
            int cyMax = Mathf.Clamp(startY + countY, 0, meshHeight);

            for (int x = cxMin; x < cxMax; x++)
            {
                for (int y = cyMin; y < cyMax; y++)
                {
                    CalculateCellNode(cellCache, x, y);
                }
            }
        }

        public void PrecalculateIncremental(TerrainCellCache cellCache, int meshWidth, int meshHeight, int dx, int dy, int worldWidth, int worldHeight)
        {
            EnsureCapacity(meshWidth, meshHeight);

            int gw = meshWidth + 1;
            int gh = meshHeight + 1;

            TerrainCellCache.Scroll2DArray(GridVertexOffsets, gw, gh, dx, dy);
            TerrainCellCache.Scroll2DArray(CellTilingDescriptors, meshWidth, meshHeight, dx, dy);
            TerrainCellCache.Scroll2DArray(CellCornerVariants, meshWidth, meshHeight, dx, dy);
            TerrainCellCache.Scroll2DArray(CellReliefMasks, meshWidth, meshHeight, dx, dy);
            TerrainCellCache.Scroll2DArray(CellSolidBoundaryMasks, meshWidth, meshHeight, dx, dy);

            int vxStart = 0, vxLen = 0, vyStart = 0, vyLen = 0;
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
                    int xStart = 0, xEnd = gw;
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

            int cxStart = 0, cxLen = 0, cyStart = 0, cyLen = 0;
            if (dx > 0)
            {
                cxStart = Mathf.Max(0, meshWidth - dx - 1);
                cxLen = meshWidth - cxStart;
            }
            else if (dx < 0)
            {
                cxStart = 0;
                cxLen = Mathf.Min(meshWidth, -dx + 1);
            }

            if (dy > 0)
            {
                cyStart = Mathf.Max(0, meshHeight - dy - 1);
                cyLen = meshHeight - cyStart;
            }
            else if (dy < 0)
            {
                cyStart = 0;
                cyLen = Mathf.Min(meshHeight, -dy + 1);
            }

            if (cxLen > 0 || cyLen > 0)
            {
                if (cxLen > 0)
                {
                    for (int x = cxStart; x < cxStart + cxLen; x++)
                    {
                        for (int y = 0; y < meshHeight; y++)
                        {
                            CalculateCellNode(cellCache, x, y);
                        }
                    }
                }

                if (cyLen > 0 && cxLen < meshWidth)
                {
                    int xStart = 0, xEnd = meshWidth;
                    if (cxLen > 0)
                    {
                        if (dx > 0)
                        {
                            xEnd = cxStart;
                        }
                        else
                        {
                            xStart = cxLen;
                        }
                    }

                    if (xStart < xEnd)
                    {
                        for (int y = cyStart; y < cyStart + cyLen; y++)
                        {
                            for (int x = xStart; x < xEnd; x++)
                            {
                                CalculateCellNode(cellCache, x, y);
                            }
                        }
                    }
                }
            }
        }

        private void CalculateVertexNode(TerrainCellCache cellCache, int x, int y, int worldWidth = int.MaxValue, int worldHeight = int.MaxValue)
        {
            if (!EnableDistortion)
            {
                GridVertexOffsets[x, y] = Vector3.zero;
                return;
            }

            int cx = x + 1;
            int cy = y + 1;
            var tl = cellCache.GetCellData(x, cy);
            var tr = cellCache.GetCellData(cx, cy);
            var bl = cellCache.GetCellData(x, y);
            var br = cellCache.GetCellData(cx, y);

            int worldX = cellCache.CacheMinX + x;
            int worldY = cellCache.CacheMinY + y;

            if (worldX <= 0 || worldX >= worldWidth || worldY <= 0 || worldY >= worldHeight)
            {
                GridVertexOffsets[x, y] = Vector3.zero;
                return;
            }

            float rx = RandXd(worldX, worldY) / 16f;
            float ry = RandYd(worldX, worldY) / 16f;

            if (IsCause(tl) && IsCause(tr) && IsCause(bl) && IsCause(br))
            {
                GridVertexOffsets[x, y] = Vector3.zero;
            }
            else if (IsBlock(tl) || IsBlock(tr) || IsBlock(bl) || IsBlock(br))
            {
                GridVertexOffsets[x, y] = Vector3.zero;
            }
            else if (worldY == 0 || (IsCause(tl) && IsCause(br)) || (IsCause(tr) && IsCause(bl)))
            {
                GridVertexOffsets[x, y] = Vector3.zero;
            }
            else if (IsCause(tl) && IsCause(tr))
            {
                GridVertexOffsets[x, y] = new Vector3(0, -ry, 0);
            }
            else if (IsCause(tl) && IsCause(bl))
            {
                GridVertexOffsets[x, y] = new Vector3(-rx, 0, 0);
            }
            else if (IsCause(tr) && IsCause(br))
            {
                GridVertexOffsets[x, y] = new Vector3(rx, 0, 0);
            }
            else if (IsCause(bl) && IsCause(br))
            {
                GridVertexOffsets[x, y] = new Vector3(0, ry, 0);
            }
            else if (IsCause(tl))
            {
                GridVertexOffsets[x, y] = new Vector3(-rx, -ry, 0);
            }
            else if (IsCause(tr))
            {
                GridVertexOffsets[x, y] = new Vector3(rx, -ry, 0);
            }
            else if (IsCause(bl))
            {
                GridVertexOffsets[x, y] = new Vector3(-rx, ry, 0);
            }
            else if (IsCause(br))
            {
                GridVertexOffsets[x, y] = new Vector3(rx, ry, 0);
            }
            else
            {
                GridVertexOffsets[x, y] = Vector3.zero;
            }
        }

        private static bool IsCause(CachedCellData data)
        {
            return data.Distortion == CellDistortionType.Cause;
        }

        private static bool IsBlock(CachedCellData data)
        {
            return data.Distortion == CellDistortionType.Block;
        }

        private static float RandXd(int x, int y)
        {
            int num = (((5 * x) + (11 * y)) * ((13 * x) + (7 * y))) % 3221;
            return (num * num) % 7;
        }

        private static float RandYd(int x, int y)
        {
            int num = (((17 * x) + (19 * y)) * ((23 * x) + (37 * y))) % 3469;
            return (num * num) % 7;
        }

        private void CalculateCellNode(TerrainCellCache cellCache, int x, int y)
        {
            int cx = x + 1;
            int cy = y + 1;
            var data = cellCache.GetCellData(cx, cy);

            var top = cellCache.GetCellData(cx, cy + 1);
            var bottom = cellCache.GetCellData(cx, cy - 1);
            var left = cellCache.GetCellData(cx - 1, cy);
            var right = cellCache.GetCellData(cx + 1, cy);
            var bottomLeft = cellCache.GetCellData(cx - 1, cy - 1);
            var bottomRight = cellCache.GetCellData(cx + 1, cy - 1);
            var topRight = cellCache.GetCellData(cx + 1, cy + 1);
            var topLeft = cellCache.GetCellData(cx - 1, cy + 1);

            if (data.HasTileGroup)
            {
                byte m = 0;
                if (left.HasTileGroup && left.TileGroupId == data.TileGroupId)
                {
                    m |= 1 << 0;
                }

                if (bottomLeft.HasTileGroup && bottomLeft.TileGroupId == data.TileGroupId)
                {
                    m |= 1 << 1;
                }

                if (bottom.HasTileGroup && bottom.TileGroupId == data.TileGroupId)
                {
                    m |= 1 << 2;
                }

                if (bottomRight.HasTileGroup && bottomRight.TileGroupId == data.TileGroupId)
                {
                    m |= 1 << 3;
                }

                if (right.HasTileGroup && right.TileGroupId == data.TileGroupId)
                {
                    m |= 1 << 4;
                }

                if (topRight.HasTileGroup && topRight.TileGroupId == data.TileGroupId)
                {
                    m |= 1 << 5;
                }

                if (top.HasTileGroup && top.TileGroupId == data.TileGroupId)
                {
                    m |= 1 << 6;
                }

                if (topLeft.HasTileGroup && topLeft.TileGroupId == data.TileGroupId)
                {
                    m |= 1 << 7;
                }

                CellTilingDescriptors[x, y] = TileBitmaskConverter.GetDescriptor(m);
            }
            else
            {
                CellTilingDescriptors[x, y] = 0;
            }

            int cornerSideMask = 0;
            if (data.Type == CellType.BuildingWall)
            {
                if (left.Type == CellType.BuildingCorner)
                {
                    cornerSideMask |= 1;
                }

                if (right.Type == CellType.BuildingCorner)
                {
                    cornerSideMask |= 2;
                }

                if (top.Type == CellType.BuildingCorner)
                {
                    cornerSideMask |= 4;
                }

                if (bottom.Type == CellType.BuildingCorner)
                {
                    cornerSideMask |= 8;
                }
            }

            CellCornerVariants[x, y] = cornerSideMask;

            byte rm = 0;
            if (top.ReliefGroup >= data.ReliefGroup)
            {
                rm |= 1;
            }

            if (left.ReliefGroup >= data.ReliefGroup)
            {
                rm |= 2;
            }

            if (bottom.ReliefGroup >= data.ReliefGroup)
            {
                rm |= 4;
            }

            if (right.ReliefGroup >= data.ReliefGroup)
            {
                rm |= 8;
            }

            CellReliefMasks[x, y] = rm;

            byte solidMask = 0;
            if ((top.Properties & CellConfigProperties.DropsShadow) != 0)
            {
                solidMask |= 1;
            }

            if ((left.Properties & CellConfigProperties.DropsShadow) != 0)
            {
                solidMask |= 2;
            }

            if ((bottom.Properties & CellConfigProperties.DropsShadow) != 0)
            {
                solidMask |= 4;
            }

            if ((right.Properties & CellConfigProperties.DropsShadow) != 0)
            {
                solidMask |= 8;
            }

            if ((topLeft.Properties & CellConfigProperties.DropsShadow) != 0)
            {
                solidMask |= 16;
            }

            if ((topRight.Properties & CellConfigProperties.DropsShadow) != 0)
            {
                solidMask |= 32;
            }

            if ((bottomLeft.Properties & CellConfigProperties.DropsShadow) != 0)
            {
                solidMask |= 64;
            }

            if ((bottomRight.Properties & CellConfigProperties.DropsShadow) != 0)
            {
                solidMask |= 128;
            }

            CellSolidBoundaryMasks[x, y] = solidMask;
        }
    }
}
