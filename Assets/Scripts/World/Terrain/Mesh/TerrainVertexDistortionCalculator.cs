#nullable enable

using Kern.Core;
using MinesServer.Data;
using MinesServer.Networking.Server.Packets.Connection;
using UnityEngine;

namespace Kern.World.Terrain;

public readonly record struct TerrainVertexOffset(float XSteps, float YSteps, float ZSteps)
{
    public const int GridSize = 32;

    public static TerrainVertexOffset Zero => new(0, 0, 0);

    public Vector3 ToVector3()
    {
        return new Vector3(
            XSteps / (float)GridSize,
            YSteps / (float)GridSize,
            ZSteps / (float)GridSize);
    }
}

public sealed class TerrainVertexDistortionCalculator
{
    // Совместимое имя для существующих проверок. Авторское значение живёт
    // вместе с остальными параметрами дисторшена в TerrainConfigHolder.
    public const int DistortionStrengthSteps = TerrainConfigHolder.ClassicDistortionStrengthSteps;

    private const float OrganicFreeJitterCenterSteps = TerrainConfigHolder.OrganicMaximumOffsetSteps / 2f;

    // Центрируется по общему диапазону классического хэша.
    private const float FreeJitterCenterSteps =
        ((TerrainConfigHolder.ClassicJitterRange - 1) / 2) * DistortionStrengthSteps;

    public TerrainRingGrid<TerrainVertexOffset> GridVertexOffsets { get; } = new();

    public bool EnableDistortion { get; set; } = true;

    public TerrainDistortionStyle DistortionStyle { get; set; } = TerrainDistortionStyle.Organic;

    public void EnsureCapacity(int meshWidth, int meshHeight)
    {
        if (GridVertexOffsets.Width != meshWidth + 1 || GridVertexOffsets.Height != meshHeight + 1)
        {
            GridVertexOffsets.EnsureSize(meshWidth + 1, meshHeight + 1);
        }
    }

    public void PrecalculateFull(ITerrainCellDataSource cellCache, int meshWidth, int meshHeight, int worldWidth, int worldHeight)
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

    public void PrecalculateRegion(ITerrainCellDataSource cellCache, int meshWidth, int meshHeight, int startX, int startY, int countX, int countY, int worldWidth, int worldHeight)
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

    public void PrecalculateIncremental(ITerrainCellDataSource cellCache, int meshWidth, int meshHeight, int dx, int dy, int worldWidth, int worldHeight)
    {
        EnsureCapacity(meshWidth, meshHeight);

        // Сетка узлов на единицу больше сетки клеток: у окна w×h ровно
        // (w+1)×(h+1) углов.
        int gw = meshWidth + 1;
        int gh = meshHeight + 1;

        GridVertexOffsets.Scroll(dx, dy);

        // Кайма в один узел: узел смещается по четырём клеткам вокруг себя, и
        // у узла на старой границе клетка снаружи только что появилась.
        TerrainScrollBands bands = TerrainScrollBands.Resolve(
            gw, gh, dx, dy, neighbourMargin: 1);
        CalculateBand(cellCache, bands.ColumnBand, worldWidth, worldHeight);
        CalculateBand(cellCache, bands.RowBand, worldWidth, worldHeight);
    }

    private void CalculateBand(
        ITerrainCellDataSource cellCache,
        RectInt band,
        int worldWidth,
        int worldHeight)
    {
        for (int x = band.xMin; x < band.xMax; x++)
        {
            for (int y = band.yMin; y < band.yMax; y++)
            {
                CalculateVertexNode(cellCache, x, y, worldWidth, worldHeight);
            }
        }
    }

    public void CalculateVertexNode(ITerrainCellDataSource cellCache, int x, int y, int worldWidth = int.MaxValue, int worldHeight = int.MaxValue)
    {
        if (!EnableDistortion)
        {
            GridVertexOffsets[x, y] = TerrainVertexOffset.Zero;
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

        GridVertexOffsets[x, y] = DistortionStyle == TerrainDistortionStyle.Organic
            ? ComputeOrganicOffset(tl, tr, bl, br, worldX, worldY, worldWidth, worldHeight)
            : ComputeOffset(tl, tr, bl, br, worldX, worldY, worldWidth, worldHeight);
    }

    public static TerrainVertexOffset ComputeOffset(
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
            return TerrainVertexOffset.Zero;
        }

        int rx = (int)RandXd(worldX, worldY) * DistortionStrengthSteps;
        int ry = (int)RandYd(worldX, worldY) * DistortionStrengthSteps;

        return ComputeOffsetFromJitter(tl, tr, bl, br, worldY, rx, ry, FreeJitterCenterSteps);
    }

    public static TerrainVertexOffset ComputeOrganicOffset(
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
            return TerrainVertexOffset.Zero;
        }

        // Узел на внешней границе массива остаётся в своей клеточной сетке.
        // Иначе смещение угла увеличивает силуэт даже при врезанных рёбрах.
        if (!IsCause(tl) || !IsCause(tr) || !IsCause(bl) || !IsCause(br))
        {
            return TerrainVertexOffset.Zero;
        }

        // Плавный шум задаёт крупную форму; дополнительные точки на рёбрах
        // задаются отдельно и не превращают весь край в прямую линию.
        float xNoise = TerrainConfigHolder.OrganicNoiseBroadWeight * ValueNoise(
                worldX, worldY, TerrainConfigHolder.OrganicNoiseBroadPeriodCells,
                TerrainConfigHolder.OrganicNoiseBroadXSeed) +
            TerrainConfigHolder.OrganicNoiseMediumWeight * ValueNoise(
                worldX, worldY, TerrainConfigHolder.OrganicNoiseMediumPeriodCells,
                TerrainConfigHolder.OrganicNoiseMediumXSeed) +
            TerrainConfigHolder.OrganicNoiseFineWeight * ValueNoise(
                worldX, worldY, TerrainConfigHolder.OrganicNoiseFinePeriodCells,
                TerrainConfigHolder.OrganicNoiseFineXSeed);
        float yNoise = TerrainConfigHolder.OrganicNoiseBroadWeight * ValueNoise(
                worldX, worldY, TerrainConfigHolder.OrganicNoiseBroadPeriodCells,
                TerrainConfigHolder.OrganicNoiseBroadYSeed) +
            TerrainConfigHolder.OrganicNoiseMediumWeight * ValueNoise(
                worldX, worldY, TerrainConfigHolder.OrganicNoiseMediumPeriodCells,
                TerrainConfigHolder.OrganicNoiseMediumYSeed) +
            TerrainConfigHolder.OrganicNoiseFineWeight * ValueNoise(
                worldX, worldY, TerrainConfigHolder.OrganicNoiseFinePeriodCells,
                TerrainConfigHolder.OrganicNoiseFineYSeed);
        float rx = Mathf.Clamp01(
            (xNoise * TerrainConfigHolder.OrganicNoiseContrast) -
            TerrainConfigHolder.OrganicNoiseCenter) *
            TerrainConfigHolder.OrganicMaximumOffsetSteps;
        float ry = Mathf.Clamp01(
            (yNoise * TerrainConfigHolder.OrganicNoiseContrast) -
            TerrainConfigHolder.OrganicNoiseCenter) *
            TerrainConfigHolder.OrganicMaximumOffsetSteps;
        return ComputeOffsetFromJitter(
            tl, tr, bl, br, worldY, rx, ry, OrganicFreeJitterCenterSteps);
    }

    private static TerrainVertexOffset ComputeOffsetFromJitter(
        CachedCellData tl,
        CachedCellData tr,
        CachedCellData bl,
        CachedCellData br,
        int worldY,
        float rx,
        float ry,
        float freeJitterCenterSteps)
    {
        // Внутри сплошного массива узел колышется свободно в обе стороны:
        // здесь нет внешней стороны, к которой нужно привязывать знак.
        if (IsCause(tl) && IsCause(tr) && IsCause(bl) && IsCause(br))
        {
            return new TerrainVertexOffset(
                rx - freeJitterCenterSteps,
                -(ry - freeJitterCenterSteps),
                0);
        }

        if (IsBlock(tl) || IsBlock(tr) || IsBlock(bl) || IsBlock(br))
        {
            return TerrainVertexOffset.Zero;
        }

        if (worldY == 0 || (IsCause(tl) && IsCause(br)) || (IsCause(tr) && IsCause(bl)))
        {
            return TerrainVertexOffset.Zero;
        }

        if (IsCause(tl) && IsCause(tr))
        {
            return new TerrainVertexOffset(0, -ry, 0);
        }

        if (IsCause(tl) && IsCause(bl))
        {
            return new TerrainVertexOffset(-rx, 0, 0);
        }

        if (IsCause(tr) && IsCause(br))
        {
            return new TerrainVertexOffset(rx, 0, 0);
        }

        if (IsCause(bl) && IsCause(br))
        {
            return new TerrainVertexOffset(0, ry, 0);
        }

        if (IsCause(tl))
        {
            return new TerrainVertexOffset(-rx, -ry, 0);
        }

        if (IsCause(tr))
        {
            return new TerrainVertexOffset(rx, -ry, 0);
        }

        if (IsCause(bl))
        {
            return new TerrainVertexOffset(-rx, ry, 0);
        }

        if (IsCause(br))
        {
            return new TerrainVertexOffset(rx, ry, 0);
        }

        return TerrainVertexOffset.Zero;
    }

    private static float ValueNoise(int worldX, int worldY, int period, uint seed)
    {
        int x0 = worldX / period;
        int y0 = worldY / period;
        float tx = (worldX % period) / (float)period;
        float ty = (worldY % period) / (float)period;
        tx = tx * tx * (3f - (2f * tx));
        ty = ty * ty * (3f - (2f * ty));
        float bottom = Mathf.Lerp(Hash01(x0, y0, seed), Hash01(x0 + 1, y0, seed), tx);
        float top = Mathf.Lerp(Hash01(x0, y0 + 1, seed), Hash01(x0 + 1, y0 + 1, seed), tx);
        return Mathf.Lerp(bottom, top, ty);
    }

    private static float Hash01(int x, int y, uint seed)
    {
        uint hash = unchecked(((uint)x * 0x9E3779B9u) ^ ((uint)y * 0x85EBCA6Bu) ^ seed);
        hash ^= hash >> 16;
        hash = unchecked(hash * 0x7FEB352Du);
        hash ^= hash >> 15;
        hash = unchecked(hash * 0x846CA68Bu);
        hash ^= hash >> 16;
        return (hash & 0x00FFFFFFu) / 16777216f;
    }

    // Одинаковый ключ у двух клеток по обе стороны ребра. Значение в шагах
    // по 2/32 кодируется в двух каналах meta вместе с остальными рёбрами.
    public static int ComputeOrganicEdgeBend(int worldX, int unityY, bool vertical)
    {
        float noise = Hash01(
            worldX, unityY,
            vertical ? TerrainConfigHolder.OrganicEdgeVerticalSeed :
                TerrainConfigHolder.OrganicEdgeHorizontalSeed);
        return Mathf.Min((int)(noise * 5f), 4) - 2;
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
        int num = (((TerrainConfigHolder.ClassicXHashA * x) +
            (TerrainConfigHolder.ClassicXHashB * y)) *
            ((TerrainConfigHolder.ClassicXHashC * x) +
            (TerrainConfigHolder.ClassicXHashD * y))) %
            TerrainConfigHolder.ClassicXHashModulus;
        return (num * num) % TerrainConfigHolder.ClassicJitterRange;
    }

    public static float RandYd(int x, int y)
    {
        int num = (((TerrainConfigHolder.ClassicYHashA * x) +
            (TerrainConfigHolder.ClassicYHashB * y)) *
            ((TerrainConfigHolder.ClassicYHashC * x) +
            (TerrainConfigHolder.ClassicYHashD * y))) %
            TerrainConfigHolder.ClassicYHashModulus;
        return (num * num) % TerrainConfigHolder.ClassicJitterRange;
    }
}
