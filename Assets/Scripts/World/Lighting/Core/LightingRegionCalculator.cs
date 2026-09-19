#nullable enable

using UnityEngine;
using Kern.World.Streaming;

namespace Kern.World.Lighting;
public static class LightingRegionCalculator
{
    private static readonly StreamingPolicy _RegionPolicy = StreamingPolicy.Default;

    // Padding is a transport requirement, not a hidden movement step. The
    // governor decides when this allocated window is replaced by containment;
    // enlarging the padding only makes each replacement more expensive.
    public static int LightingRegionPaddingCells =>
        StreamingPolicy.DefaultLightingPaddingCells;

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
            bool viewportInsideRegion = _RegionPolicy.ContainsViewport(
                new Vector2Int(regionWidth, regionHeight),
                new Vector2Int(visibleMinX - currentMinX, visibleMinY - currentMinY),
                new Vector2Int(visibleWidth, visibleHeight));

            if (viewportInsideRegion)
            {
                return lastVisibleRegion;
            }
        }

        int paddedMinX = _RegionPolicy.AlignOrigin(
            visibleMinX - LightingRegionPaddingCells);
        int paddedMinY = _RegionPolicy.AlignOrigin(
            visibleMinY - LightingRegionPaddingCells);

        // Размер зависит только от viewport и padding. Origin не привязан к
        // искусственной сетке: перепривязка происходит только когда viewport
        // действительно вышел за текущее стабильное окно.
        int alignmentSlack = Mathf.Max(0, _RegionPolicy.AllocationQuantumCells - 1);
        int requiredWidth = visibleWidth + (LightingRegionPaddingCells * 2) + alignmentSlack;
        int requiredHeight = visibleHeight + (LightingRegionPaddingCells * 2) + alignmentSlack;
        // Lighting pays for the whole field on every static solve. The
        // viewport padding already provides a stable window; adding another
        // allocation quantum here increases a single solve quadratically.
        // Terrain keeps its own headroom because its scroll path is cheap,
        // while lighting must minimize the worst GPU burst.
        int paddedWidth = _RegionPolicy.QuantizeDimension(requiredWidth);
        int paddedHeight = _RegionPolicy.QuantizeDimension(requiredHeight);

        // Размер — high-water mark. Уменьшение поля во время ходьбы меняет
        // все cascade resources и запускает ещё один полный static solve.
        // Сжать поле можно только отдельным resize-путём quality/config.
        if (!float.IsNaN(lastVisibleRegion.x))
        {
            paddedWidth = _RegionPolicy.QuantizeDimension(
                Mathf.Max(paddedWidth, Mathf.RoundToInt(lastVisibleRegion.z)));
            paddedHeight = _RegionPolicy.QuantizeDimension(
                Mathf.Max(paddedHeight, Mathf.RoundToInt(lastVisibleRegion.w)));
        }

        return new Vector4(
            paddedMinX,
            paddedMinY,
            Mathf.Max(2, paddedWidth),
            Mathf.Max(2, paddedHeight));
    }
}
