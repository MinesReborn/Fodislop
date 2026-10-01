#nullable enable

using System;
using System.Collections.Generic;
using Kern.Rendering;
using UnityEngine;

namespace Kern.World.Lighting;

// Validates a proposed resource layout before the resource owner mutates a live generation.
internal static class LightingResourceLayout
{
    public static void Validate(int gridWidth, int gridHeight, LightingQualityTuning quality,
        in GraphicsQualitySettings settings, List<CascadeLayout> candidate)
    {
        LightingQualityTuningController.Validate(quality);
        int width = checked(gridWidth * quality.FieldPixelsPerCell);
        int height = checked(gridHeight * quality.FieldPixelsPerCell);
        int maximumDimension = Mathf.Min(SystemInfo.maxTextureSize, settings.LightingMaximumTextureDimension);
        if (width > maximumDimension || height > maximumDimension)
        {
            throw new InvalidOperationException($"Поле {width}×{height} при {quality.FieldPixelsPerCell} пикс/клетку " +
                $"не помещается: предел {maximumDimension}. Выбери плотность явно.");
        }
        int aoWidth = checked(gridWidth * LightingConfigHolder.AmbientOcclusionPixelsPerCell);
        int aoHeight = checked(gridHeight * LightingConfigHolder.AmbientOcclusionPixelsPerCell);
        if (aoWidth > SystemInfo.maxTextureSize || aoHeight > SystemInfo.maxTextureSize)
        {
            throw new InvalidOperationException($"AO requires {aoWidth}x{aoHeight}; GPU limit is {SystemInfo.maxTextureSize}.");
        }
        CascadeLayoutBuilder.BuildCascadeLayouts(width, height, settings.LightingCascadeAtlasLimit, candidate,
            quality.MaximumStaticCascadeDirections, quality.FieldPixelsPerCell / quality.CascadeProbePixelsPerCell);
        long entries = (long)candidate[^1].Offset + candidate[^1].EntryCount;
        long maximumEntries = checked((long)settings.LightingCascadeAtlasLimit * settings.LightingCascadeAtlasLimit * 4);
        if (entries > maximumEntries)
        {
            throw new InvalidOperationException($"Атлас требует {entries} записей при {quality.CascadeProbePixelsPerCell} " +
                $"проб/клетку и {quality.MaximumStaticCascadeDirections} направлениях; предел {maximumEntries}. " +
                "Выбери качество транспорта явно.");
        }
    }

}
