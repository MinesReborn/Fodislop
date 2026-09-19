#nullable enable

using System;
using MinesServer.Data;
using MinesServer.Networking.Server.Packets.Connection;

namespace Kern.World.Terrain;

// Подмена TerrainCellCache для бенчмарка: тот же API, который читают
// калькуляторы масок и искажения, но клетки берутся из синтетической карты,
// а не из хранилища мира и MapManager. Кэш на одну клетку шире сетки с каждой
// стороны, как настоящий.
public class TerrainCellCache
{
    private readonly TerrainRingGrid<CachedCellData> _cells = new();

    public int CacheMinX { get; private set; }

    public int CacheMinY { get; private set; }

    public int CacheWidth { get; private set; }

    public int CacheHeight { get; private set; }

    public CachedCellData GetCellData(int x, int y)
    {
        if ((uint)x >= (uint)CacheWidth || (uint)y >= (uint)CacheHeight)
        {
            return new CachedCellData { State = TerrainCellState.OutsideWorld, Type = CellType.Unloaded };
        }

        return _cells[x, y];
    }

    // Пещеры: шум из хеша, сплошные массы с полостями и дорогами.
    public void FillCaves(int meshWidth, int meshHeight, int minX, int minY, int seed)
    {
        CacheWidth = meshWidth + 2;
        CacheHeight = meshHeight + 2;
        CacheMinX = minX - 1;
        CacheMinY = minY - 1;
        _cells.EnsureSize(CacheWidth, CacheHeight);
        for (int x = 0; x < CacheWidth; x++)
        {
            for (int y = 0; y < CacheHeight; y++)
            {
                _cells[x, y] = CellAt(CacheMinX + x, CacheMinY + y, seed);
            }
        }
    }

    // Как ScrollAndFill: сдвиг массива и дозаполнение вошедшей каймы.
    public void ScrollTo(int minX, int minY, int seed)
    {
        int dx = (minX - 1) - CacheMinX;
        int dy = (minY - 1) - CacheMinY;
        if (Math.Abs(dx) >= CacheWidth || Math.Abs(dy) >= CacheHeight)
        {
            FillCaves(CacheWidth - 2, CacheHeight - 2, minX, minY, seed);
            return;
        }

        _cells.Scroll(dx, dy);
        CacheMinX += dx;
        CacheMinY += dy;
        int columnStart = dx > 0 ? CacheWidth - dx : 0;
        for (int x = columnStart; x < columnStart + Math.Abs(dx); x++)
        {
            for (int y = 0; y < CacheHeight; y++)
            {
                _cells[x, y] = CellAt(CacheMinX + x, CacheMinY + y, seed);
            }
        }

        int rowStart = dy > 0 ? CacheHeight - dy : 0;
        for (int y = rowStart; y < rowStart + Math.Abs(dy); y++)
        {
            for (int x = 0; x < CacheWidth; x++)
            {
                _cells[x, y] = CellAt(CacheMinX + x, CacheMinY + y, seed);
            }
        }
    }

    // Выкопанный прямоугольник в локальных координатах сетки (без каймы).
    public void Dig(int x, int y, int width, int height)
    {
        for (int cx = x; cx < x + width; cx++)
        {
            for (int cy = y; cy < y + height; cy++)
            {
                ref CachedCellData cell = ref _cells[cx + 1, cy + 1];
                cell.Type = CellType.Empty;
                cell.Properties = CellConfigProperties.Passable;
                cell.Distortion = 0;
                cell.ReliefGroup = 0;
            }
        }
    }

    public static CachedCellData CellAt(int worldX, int worldY, int seed)
    {
        uint hash = (uint)((worldX * 374761393) ^ (worldY * 668265263) ^ (seed * 1274126177));
        hash = (hash ^ (hash >> 13)) * 1274126177;
        int noise = (int)((hash ^ (hash >> 16)) % 100);
        bool solid = noise < 62;
        bool road = !solid && noise > 94;
        CellType type = solid ? (CellType)(10 + (noise % 6)) : road ? CellType.Road : CellType.Empty;
        CellConfigProperties properties = solid ? 0 : CellConfigProperties.Passable;
        return new CachedCellData
        {
            State = TerrainCellState.Loaded,
            Type = type,
            Properties = properties,
            ReliefGroup = (byte)(solid ? 1 + (noise % 3) : 0),
            Distortion = solid && noise % 7 == 0 ? (CellDistortionType)1 : 0,
            HasTileGroup = solid && noise % 5 == 0,
            TileGroupID = solid ? noise % 4 : 0,
            AtlasIndex = 0,
            IsTextureReady = true,
        };
    }
}
