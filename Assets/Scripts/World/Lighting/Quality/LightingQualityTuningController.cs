#nullable enable

using System;

namespace Kern.World.Lighting;

/// <summary>Lighting-owned session settings; applying data does not run terrain or GPU work.</summary>
public static class LightingQualityTuningController
{
    public static LightingQualityTuning Current { get; private set; } = LightingConfigHolder.DefaultQuality;
    public static ulong Revision { get; private set; }

    static LightingQualityTuningController()
    {
        Validate(Current);
    }

    public static int FieldPixelsPerCell => Current.FieldPixelsPerCell;
    public static int LightPixelsPerCell => Current.LightPixelsPerCell;
    public static int CascadeProbePixelsPerCell => Current.CascadeProbePixelsPerCell;
    public static int MaximumStaticCascadeDirections => Current.MaximumStaticCascadeDirections;
    public static float DynamicNearCells => Current.DynamicNearCells;
    public static int DynamicAngularSampleCount => Current.DynamicAngularSampleCount;
    public static int DynamicEmitterPointsPerAxis => Current.DynamicEmitterPointsPerAxis;
    public static int DynamicPolarDirectionCount => Current.DynamicPolarDirectionCount;

    public static void Apply(LightingQualityTuning quality)
    {
        Validate(quality);
        if (quality == Current)
        {
            return;
        }
        ulong revision = checked(Revision + 1);
        Current = quality;
        Revision = revision;
    }

    public static void Validate(LightingQualityTuning quality)
    {
        RequirePowerOfTwo(quality.FieldPixelsPerCell, 1, 32, nameof(quality.FieldPixelsPerCell));
        RequirePowerOfTwo(quality.LightPixelsPerCell, 1, quality.FieldPixelsPerCell, nameof(quality.LightPixelsPerCell));
        RequirePowerOfTwo(quality.CascadeProbePixelsPerCell, 1,
            Math.Min(16, quality.FieldPixelsPerCell), nameof(quality.CascadeProbePixelsPerCell));
        RequirePowerOfTwo(quality.MaximumStaticCascadeDirections, 4, 64, nameof(quality.MaximumStaticCascadeDirections));
        RequirePowerOfTwo(quality.DynamicPolarDirectionCount, 4, 1024, nameof(quality.DynamicPolarDirectionCount));
        if (float.IsNaN(quality.DynamicNearCells) || float.IsInfinity(quality.DynamicNearCells) ||
            quality.DynamicNearCells < 0.5f || quality.DynamicNearCells > 8f)
        {
            throw new ArgumentOutOfRangeException(nameof(quality.DynamicNearCells),
                "Ближняя зона должна быть от 0.5 до 8 клеток.");
        }
        if (quality.DynamicAngularSampleCount is < 1 or > 64)
        {
            throw new ArgumentOutOfRangeException(nameof(quality.DynamicAngularSampleCount));
        }
        if (quality.DynamicEmitterPointsPerAxis is < 1 or > 4)
        {
            throw new ArgumentOutOfRangeException(nameof(quality.DynamicEmitterPointsPerAxis));
        }
    }

    private static void RequirePowerOfTwo(int value, int minimum, int maximum, string name)
    {
        if (value < minimum || value > maximum || (value & (value - 1)) != 0)
        {
            throw new ArgumentOutOfRangeException(name,
                $"Значение должно быть степенью двойки от {minimum} до {maximum}.");
        }
    }
}
