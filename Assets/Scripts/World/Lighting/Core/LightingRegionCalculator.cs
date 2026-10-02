#nullable enable

using UnityEngine;
using Kern.World.Streaming;

namespace Kern.World.Lighting;
public static class LightingRegionCalculator
{
    private static readonly StreamingPolicy s_regionPolicy = StreamingPolicy.Default;

    // Padding is a transport requirement, not a hidden movement step. The
    // governor decides when this allocated window is replaced by containment;
    // enlarging the padding only makes each replacement more expensive.
    public static int LightingRegionPaddingCells =>
        StreamingPolicy.DefaultLightingPaddingCells;

    /// <summary>Кайма террейна под динамические источники.</summary>
    ///
    /// Dynamic sources are rasterized as one-cell emitters. Their propagation
    /// distance is solved by the same extinction and cascade intervals as
    /// terrain emission, not by a source halo.
    public static int TerrainPaddingCells => 3;

    /// <summary>Регион задевает стабильное окно — с каймой ровно в одну клетку.</summary>
    ///
    /// Кайма симметрична: клетка сразу за кромкой окна его ещё задевает (её
    /// правка меняет и соседнюю клетку внутри), а вторая клетка — уже нет.
    /// Поэтому справа и снизу граница — `x + z`, а не `x + z + 1`: с плюс
    /// единицей запас выходил в две клетки против одной слева, и правка второй
    /// клетки за окном запускала полный пересчёт зря. Стережёт
    /// TouchesStableRegionIncludesExactlyOneCellMargin.
    public static bool TouchesStableRegion(
        int worldX,
        int worldY,
        int width,
        int height,
        Vector4 stableRegion)
    {
        if (float.IsNaN(stableRegion.x))
        {
            return true;
        }

        int regionMaxX = worldX + width - 1;
        int regionMaxY = worldY + height - 1;
        return regionMaxX >= stableRegion.x - 1f &&
            worldX <= stableRegion.x + stableRegion.z &&
            regionMaxY >= stableRegion.y - 1f &&
            worldY <= stableRegion.y + stableRegion.w;
    }

    public static Vector4 GetStableLightingRegion(
        int visibleMinX,
        int visibleMinY,
        int visibleWidth,
        int visibleHeight,
        Vector4 lastVisibleRegion)
    {
        if (!float.IsNaN(lastVisibleRegion.x))
        {
            int currentMinX = Mathf.RoundToInt(lastVisibleRegion.x);
            int currentMinY = Mathf.RoundToInt(lastVisibleRegion.y);
            int regionWidth = Mathf.RoundToInt(lastVisibleRegion.z);
            int regionHeight = Mathf.RoundToInt(lastVisibleRegion.w);
            bool viewportInsideRegion = s_regionPolicy.ContainsViewport(
                new Vector2Int(regionWidth, regionHeight),
                new Vector2Int(visibleMinX - currentMinX, visibleMinY - currentMinY),
                new Vector2Int(visibleWidth, visibleHeight));

            if (viewportInsideRegion)
            {
                return lastVisibleRegion;
            }
        }

        int paddedMinX = s_regionPolicy.AlignOrigin(
            visibleMinX - LightingRegionPaddingCells);
        int paddedMinY = s_regionPolicy.AlignOrigin(
            visibleMinY - LightingRegionPaddingCells);

        // Размер зависит только от viewport и padding. Origin не привязан к
        // искусственной сетке: перепривязка происходит только когда viewport
        // действительно вышел за текущее стабильное окно.
        int alignmentSlack = Mathf.Max(0, s_regionPolicy.AllocationQuantumCells - 1);
        int requiredWidth = visibleWidth + (LightingRegionPaddingCells * 2) + alignmentSlack;
        int requiredHeight = visibleHeight + (LightingRegionPaddingCells * 2) + alignmentSlack;
        // Lighting pays for the whole field on every static solve. The
        // viewport padding already provides a stable window; adding another
        // allocation quantum here increases a single solve quadratically.
        // Terrain keeps its own headroom because its scroll path is cheap,
        // while lighting must minimize the worst GPU burst.
        int paddedWidth = s_regionPolicy.QuantizeDimension(requiredWidth);
        int paddedHeight = s_regionPolicy.QuantizeDimension(requiredHeight);

        // Размер — high-water mark. Уменьшение поля во время ходьбы меняет
        // все cascade resources и запускает ещё один полный static solve.
        // Сжать поле можно только отдельным resize-путём quality/config.
        if (!float.IsNaN(lastVisibleRegion.x))
        {
            paddedWidth = s_regionPolicy.QuantizeDimension(
                Mathf.Max(paddedWidth, Mathf.RoundToInt(lastVisibleRegion.z)));
            paddedHeight = s_regionPolicy.QuantizeDimension(
                Mathf.Max(paddedHeight, Mathf.RoundToInt(lastVisibleRegion.w)));
        }

        return new Vector4(
            paddedMinX,
            paddedMinY,
            Mathf.Max(2, paddedWidth),
            Mathf.Max(2, paddedHeight));
    }
}
