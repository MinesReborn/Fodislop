#nullable enable

using MinesServer.Data;
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

/// <summary>Включено ли классическое искажение сетки.</summary>
public sealed class TerrainDistortionSettings
{
    public bool EnableDistortion { get; set; } = true;

}

// Смещение узла сетки — левого нижнего угла клетки — по четырём клеткам
// вокруг узла и его мировой координате.
//
// Узлы не хранятся: шейдер считает их тем же правилом из соседей в буфере
// клеток (TerrainCellData.hlsl, TerrainNode). Здесь — CPU-сторона того же
// правила для эталона тестов. Всё целыми числами, поэтому CPU и GPU дают
// один и тот же узел бит в бит.
public static class TerrainVertexDistortionCalculator
{
    // Совместимое имя для существующих проверок. Авторское значение живёт
    // вместе с остальными параметрами дисторшена в TerrainConfigHolder.
    public const float DistortionStrengthSteps = TerrainConfigHolder.ClassicDistortionStrengthSteps;

    // Единица узла — 1/8 шага (1/256 клетки): в ней узел точен на CPU и GPU.
    public const int UnitsPerStep = TerrainCellData.GeometryUnitsPerCell / TerrainVertexOffset.GridSize;

    public const int DistortionStrengthUnits = (int)(DistortionStrengthSteps * UnitsPerStep);

    // Центрируется по общему диапазону классического хэша.
    private const int FreeJitterCenterUnits =
        ((TerrainConfigHolder.ClassicJitterRange - 1) / 2) * DistortionStrengthUnits;

    /// <summary>Узел (x, y) окна: углы клеток кэша x..x+1, y..y+1.</summary>
    public static TerrainVertexOffset ComputeNode(
        ITerrainCellDataSource cellCache,
        TerrainDistortionSettings settings,
        int x,
        int y,
        int worldWidth,
        int worldHeight)
    {
        if (!settings.EnableDistortion)
        {
            return TerrainVertexOffset.Zero;
        }

        int cx = x + 1;
        int cy = y + 1;
        CachedCellData tl = cellCache.GetCellData(x, cy);
        CachedCellData tr = cellCache.GetCellData(cx, cy);
        CachedCellData bl = cellCache.GetCellData(x, y);
        CachedCellData br = cellCache.GetCellData(cx, y);
        int worldX = cellCache.CacheMinX + x;
        int worldY = cellCache.CacheMinY + y;
        return ComputeOffset(tl, tr, bl, br, worldX, worldY, worldWidth, worldHeight);
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
        _ = worldWidth;
        _ = worldHeight;
        int rx = RandXd(worldX, worldY) * DistortionStrengthUnits;
        int ry = RandYd(worldX, worldY) * DistortionStrengthUnits;
        return ComputeOffsetFromJitter(tl, tr, bl, br, worldY, rx, ry, FreeJitterCenterUnits);
    }

    public static bool IsWavy(CachedCellData data)
    {
        return !data.IsBackground && data.Outline == CellOutline.Wavy;
    }

    // Контур фона не участвует в узлах геометрии переднего плана.
    public static bool Holds(CachedCellData data)
    {
        return !data.IsBackground && data.Outline is not (CellOutline.Pliant or CellOutline.Wavy);
    }

    public static int RandXd(int x, int y)
    {
        int num = unchecked(((TerrainConfigHolder.ClassicXHashA * x) +
            (TerrainConfigHolder.ClassicXHashB * y)) *
            ((TerrainConfigHolder.ClassicXHashC * x) +
            (TerrainConfigHolder.ClassicXHashD * y))) %
            TerrainConfigHolder.ClassicXHashModulus;
        return (num * num) % TerrainConfigHolder.ClassicJitterRange;
    }

    public static int RandYd(int x, int y)
    {
        int num = unchecked(((TerrainConfigHolder.ClassicYHashA * x) +
            (TerrainConfigHolder.ClassicYHashB * y)) *
            ((TerrainConfigHolder.ClassicYHashC * x) +
            (TerrainConfigHolder.ClassicYHashD * y))) %
            TerrainConfigHolder.ClassicYHashModulus;
        return (num * num) % TerrainConfigHolder.ClassicJitterRange;
    }

    // rx, ry и центр — в единицах узла.
    private static TerrainVertexOffset ComputeOffsetFromJitter(
        CachedCellData tl,
        CachedCellData tr,
        CachedCellData bl,
        CachedCellData br,
        int worldY,
        int rx,
        int ry,
        int freeJitterCenter)
    {
        // Внутри массы узел колышется свободно в обе стороны:
        // здесь нет внешней стороны, к которой нужно привязывать знак.
        if (IsWavy(tl) && IsWavy(tr) && IsWavy(bl) && IsWavy(br))
        {
            return Units(rx - freeJitterCenter, -(ry - freeJitterCenter));
        }

        if (Holds(tl) || Holds(tr) || Holds(bl) || Holds(br))
        {
            return TerrainVertexOffset.Zero;
        }

        if (worldY == 0 || (IsWavy(tl) && IsWavy(br)) || (IsWavy(tr) && IsWavy(bl)))
        {
            return TerrainVertexOffset.Zero;
        }

        if (IsWavy(tl) && IsWavy(tr))
        {
            return Units(0, -ry);
        }

        if (IsWavy(tl) && IsWavy(bl))
        {
            return Units(-rx, 0);
        }

        if (IsWavy(tr) && IsWavy(br))
        {
            return Units(rx, 0);
        }

        if (IsWavy(bl) && IsWavy(br))
        {
            return Units(0, ry);
        }

        if (IsWavy(tl))
        {
            return Units(-rx, -ry);
        }

        if (IsWavy(tr))
        {
            return Units(rx, -ry);
        }

        if (IsWavy(bl))
        {
            return Units(-rx, ry);
        }

        if (IsWavy(br))
        {
            return Units(rx, ry);
        }

        return TerrainVertexOffset.Zero;
    }

    private static TerrainVertexOffset Units(int x, int y) =>
        new(x / (float)UnitsPerStep, y / (float)UnitsPerStep, 0f);
}
