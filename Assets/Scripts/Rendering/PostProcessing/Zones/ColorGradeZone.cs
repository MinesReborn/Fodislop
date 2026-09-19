#nullable enable

using System;
using UnityEngine;

namespace Kern.Rendering.PostProcessing;
public readonly record struct ColorGradeZone
{
    public ColorGradeZone(
        string name,
        float centerY,
        float halfHeight,
        float feather,
        ColorGradeSnapshot grade,
        float exposure = PostProcessLook.ColorGrading.Exposure,
        float contrast = PostProcessLook.ColorGrading.Contrast,
        float saturation = PostProcessLook.ColorGrading.Saturation,
        float centerX = 0f,
        float halfWidth = float.PositiveInfinity)
    {
        Name = name;
        CenterY = centerY;
        HalfHeight = halfHeight;
        Feather = feather;
        Exposure = exposure;
        Contrast = contrast;
        Saturation = saturation;
        Grade = grade;
        CenterX = centerX;
        HalfWidth = halfWidth;
    }

    public string Name { get; init; }

    public float CenterY { get; init; }

    public float HalfHeight { get; init; }

    public float Feather { get; init; }

    public float CenterX { get; init; } = 0f;

    public float HalfWidth { get; init; } = float.PositiveInfinity;

    public float Exposure { get; init; }

    public float Contrast { get; init; }

    public float Saturation { get; init; }

    public ColorGradeSnapshot Grade { get; init; }

    public float WeightAt(float worldY) => WeightAt(0f, worldY);

    public float WeightAt(float worldX, float worldY)
    {
        if (float.IsNaN(worldY) || float.IsNaN(worldX))
        {
            return 0f;
        }

        float distanceY = Mathf.Abs(worldY - CenterY);
        float coreY = Mathf.Max(HalfHeight, 0f);
        float feather = Mathf.Max(Feather, 0f);

        float weightY;
        if (distanceY <= coreY)
        {
            weightY = 1f;
        }
        else if (feather <= 0f || distanceY >= coreY + feather)
        {
            return 0f;
        }
        else
        {
            weightY = Mathf.SmoothStep(1f, 0f, (distanceY - coreY) / feather);
        }

        if (float.IsPositiveInfinity(HalfWidth) || HalfWidth <= 0f)
        {
            return weightY;
        }

        float distanceX = Mathf.Abs(worldX - CenterX);
        float coreX = Mathf.Max(HalfWidth, 0f);
        if (distanceX <= coreX)
        {
            return weightY;
        }

        if (feather <= 0f || distanceX >= coreX + feather)
        {
            return 0f;
        }

        float weightX = Mathf.SmoothStep(1f, 0f, (distanceX - coreX) / feather);
        return weightX * weightY;
    }

    public ColorGradeZone Sanitized() => new()
    {
        Name = string.IsNullOrWhiteSpace(Name) ? "зона" : Name,
        CenterY = float.IsNaN(CenterY) || float.IsInfinity(CenterY) ? 0f : CenterY,
        HalfHeight = Finite(HalfHeight),
        Feather = Finite(Feather),
        CenterX = float.IsNaN(CenterX) || float.IsInfinity(CenterX) ? 0f : CenterX,
        HalfWidth = float.IsNaN(HalfWidth) ? float.PositiveInfinity : (HalfWidth < 0f ? 0f : HalfWidth),
        Exposure = FiniteClamp(
            Exposure,
            ColorGradeState.ExposureMin,
            ColorGradeState.ExposureMax,
            PostProcessLook.ColorGrading.Exposure),
        Contrast = FiniteClamp(
            Contrast,
            ColorGradeState.ContrastMin,
            ColorGradeState.ContrastMax,
            PostProcessLook.ColorGrading.Contrast),
        Saturation = FiniteClamp(
            Saturation,
            ColorGradeState.SaturationMin,
            ColorGradeState.SaturationMax,
            PostProcessLook.ColorGrading.Saturation),
        Grade = Grade.Sanitized(),
    };

    private static float Finite(float value) =>
        float.IsNaN(value) || float.IsInfinity(value) ? 0f : Math.Max(value, 0f);

    private static float FiniteClamp(
        float value, float minimum, float maximum, float fallback) =>
        float.IsNaN(value) || float.IsInfinity(value)
            ? fallback
            : Mathf.Clamp(value, minimum, maximum);
}
