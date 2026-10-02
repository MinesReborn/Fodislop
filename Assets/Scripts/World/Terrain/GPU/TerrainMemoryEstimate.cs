#nullable enable

using System;

namespace Kern.World.Terrain;

/// <summary>
/// Управляемая оценка памяти текущей схемы публикации terrain.
///
/// На клетку приходится две строки каналов: фон и передний план. Форматы
/// соответствуют TerrainCellDataTextures: два RGBA32, пять RGBAHalf и два
/// RGBAFloat. Копия на CPU одна: фоновый шаг пишет в неё, пока GPU держит
/// прежнюю версию, а в кадре публикации изменённые прямоугольники уходят
/// через staging. Второго комплекта GPU-каналов нет — согласованность дают
/// одна публикация за кадр и владение CPU-копией рабочим потоком до неё.
/// </summary>
public readonly record struct TerrainMemoryEstimate(
    long GPUTargetBytes,
    long GPUStagingBytes,
    long CPUTexelBytes)
{
    public long PeakBytes => checked(GPUTargetBytes + GPUStagingBytes + CPUTexelBytes);

    public static TerrainMemoryEstimate ForWindow(
        int width,
        int height,
        int stagingRows = 128)
    {
        if (width <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(width));
        }

        if (height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(height));
        }

        if (stagingRows <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(stagingRows));
        }

        const long BytesPerCellPerLayer =
            (2L * 4L) + // Color32 + meta
            (5L * 8L) + // five RGBAHalf channels
            (2L * 16L); // world + glow RGBAFloat
        long targetTexelCount = checked((long)width * height * 2);
        long stagingTexelCount = checked((long)width * Math.Min(height * 2, stagingRows));
        long targetBytes = checked(targetTexelCount * BytesPerCellPerLayer);
        long stagingBytes = checked(stagingTexelCount * BytesPerCellPerLayer);

        return new TerrainMemoryEstimate(targetBytes, stagingBytes, targetBytes);
    }
}
