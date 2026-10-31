#nullable enable

using System;
using System.Collections.Generic;
using Kern.Core;
using Kern.Core.Diagnostics;
using Kern.Core.Interfaces;
using Kern.Rendering;
using Kern.World.Lighting.Quality;
using UnityEngine;
using UnityEngine.Rendering;
using Kern.Diagnostics.Memory;

namespace Kern.World.Lighting;

internal static class LightingResourceQuality
{
    internal static void ValidateRequestedQuality(
        int gridWidth, int gridHeight,
        LightingQualityTuning quality,
        in GraphicsQualitySettings settings)
    {
        if (gridWidth <= 0 || gridHeight <= 0)
        {
            throw new InvalidOperationException("Дождись готовности мирового поля перед применением качества.");
        }
        LightingResourceLayout.Validate(gridWidth, gridHeight, quality, settings, new List<CascadeLayout>());
    }
    internal static bool TryApplyQualityTuning(
        int gridWidth, int gridHeight,
        LightingQualityTuning quality,
        in GraphicsQualitySettings settings,
        out string rejection)
    {
        try
        {
            ValidateRequestedQuality(gridWidth, gridHeight, quality, settings);
            LightingQualityTuningController.Apply(quality);
            rejection = string.Empty;
            return true;
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or OverflowException)
        {
            rejection = exception.Message;
            return false;
        }
    }
}
