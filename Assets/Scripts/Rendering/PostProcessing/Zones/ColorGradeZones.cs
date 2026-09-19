#nullable enable

using System.Collections.Generic;
using UnityEngine;

namespace Kern.Rendering.PostProcessing;
public sealed class ColorGradeZones
{
    public readonly record struct Resolution(
        ColorGradeSnapshot Grade,
        float Exposure,
        float Contrast,
        float Saturation)
    {
        public static Resolution FromLook() => new(
            ColorGradeSnapshot.FromLook(),
            PostProcessLook.ColorGrading.Exposure,
            PostProcessLook.ColorGrading.Contrast,
            PostProcessLook.ColorGrading.Saturation);

        public Resolution BlendTo(Resolution other, float weight)
        {
            float t = float.IsNaN(weight) ? 0f : Mathf.Clamp01(weight);
            if (t <= 0f)
            {
                return this;
            }

            if (t >= 1f)
            {
                return other;
            }

            return new Resolution(
                Grade.BlendTo(other.Grade, t),
                Mathf.Lerp(Exposure, other.Exposure, t),
                Mathf.Lerp(Contrast, other.Contrast, t),
                Mathf.Lerp(Saturation, other.Saturation, t));
        }
    }

    private readonly List<ColorGradeZone> _zones = new();

    public IReadOnlyList<ColorGradeZone> Zones => _zones;

    public int Count => _zones.Count;

    public bool Enabled { get; set; }

    public void Clear() => _zones.Clear();

    public void Add(ColorGradeZone zone) => _zones.Add(zone.Sanitized());

    public void RemoveAt(int index)
    {
        if (index >= 0 && index < _zones.Count)
        {
            _zones.RemoveAt(index);
        }
    }

    public void Replace(int index, ColorGradeZone zone)
    {
        if (index >= 0 && index < _zones.Count)
        {
            _zones[index] = zone.Sanitized();
        }
    }

    public Resolution Resolve(Resolution baseGrade, float worldY) =>
        Resolve(baseGrade, 0f, worldY);

    public Resolution Resolve(Resolution baseGrade, float worldX, float worldY)
    {
        if (!Enabled || _zones.Count == 0)
        {
            return baseGrade;
        }

        Resolution result = baseGrade;
        for (int index = 0; index < _zones.Count; index++)
        {
            ColorGradeZone zone = _zones[index];
            float weight = zone.WeightAt(worldX, worldY);
            if (weight > 0f)
            {
                result = result.BlendTo(
                    new Resolution(
                        zone.Grade,
                        zone.Exposure,
                        zone.Contrast,
                        zone.Saturation),
                    weight);
            }
        }

        return result;
    }

    public string DescribeAt(float worldY) => DescribeAt(0f, worldY);

    public string DescribeAt(float worldX, float worldY)
    {
        string name = "база";
        float best = 0f;
        for (int index = 0; index < _zones.Count; index++)
        {
            float weight = _zones[index].WeightAt(worldX, worldY);
            if (weight > best)
            {
                best = weight;
                name = _zones[index].Name;
            }
        }

        return best > 0f ? $"{name} ({best:P0})" : name;
    }
}
