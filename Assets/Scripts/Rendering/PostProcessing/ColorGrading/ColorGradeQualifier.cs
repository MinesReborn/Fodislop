#nullable enable

using System.Collections.Generic;
using UnityEngine;

namespace Kern.Rendering.PostProcessing;

public sealed class ColorGradeQualifier
{
    public const int MaxHueSamples = 8;

    private readonly List<float> _hueSamples = [];

    public bool Enabled { get; set; }

    public bool Invert { get; set; }

    public float HueCenter { get; set; } = PostProcessLook.Qualifier.HueCenter;
    public float HueWidth { get; set; } = PostProcessLook.Qualifier.HueWidth;
    public float HueSoftness { get; set; } = PostProcessLook.Qualifier.HueSoftness;
    public float SaturationCenter { get; set; } = PostProcessLook.Qualifier.SaturationCenter;
    public float SaturationWidth { get; set; } = PostProcessLook.Qualifier.SaturationWidth;
    public float SaturationSoftness { get; set; } = PostProcessLook.Qualifier.SaturationSoftness;
    public float LuminanceCenter { get; set; } = PostProcessLook.Qualifier.LuminanceCenter;
    public float LuminanceWidth { get; set; } = PostProcessLook.Qualifier.LuminanceWidth;
    public float LuminanceSoftness { get; set; } = PostProcessLook.Qualifier.LuminanceSoftness;

    public float HueShift { get; set; }
    public float Saturation { get; set; } = PostProcessLook.ColorGrading.Saturation;
    public float Exposure { get; set; }
    public float Temperature { get; set; }
    public float Tint { get; set; }
    public Vector3 Lift { get; set; }
    public Vector3 Gamma { get; set; } = PostProcessLook.Grade.PrimaryGamma;
    public Vector3 Gain { get; set; } = PostProcessLook.Grade.PrimaryGain;

    public IReadOnlyList<float> HueSamples => _hueSamples;

    public void AddHueSample(float center)
    {
        if (_hueSamples.Count < MaxHueSamples && float.IsFinite(center))
        {
            _hueSamples.Add(Mathf.Repeat(center, 360f));
        }
    }

    public bool RemoveLastHueSample()
    {
        if (_hueSamples.Count == 0)
        {
            return false;
        }

        _hueSamples.RemoveAt(_hueSamples.Count - 1);
        return true;
    }

    public void ClearHueSamples() => _hueSamples.Clear();

    public void Reset()
    {
        Enabled = false;
        Invert = false;
        HueCenter = PostProcessLook.Qualifier.HueCenter;
        HueWidth = PostProcessLook.Qualifier.HueWidth;
        HueSoftness = PostProcessLook.Qualifier.HueSoftness;
        SaturationCenter = PostProcessLook.Qualifier.SaturationCenter;
        SaturationWidth = PostProcessLook.Qualifier.SaturationWidth;
        SaturationSoftness = PostProcessLook.Qualifier.SaturationSoftness;
        LuminanceCenter = PostProcessLook.Qualifier.LuminanceCenter;
        LuminanceWidth = PostProcessLook.Qualifier.LuminanceWidth;
        LuminanceSoftness = PostProcessLook.Qualifier.LuminanceSoftness;
        HueShift = 0f;
        Saturation = PostProcessLook.ColorGrading.Saturation;
        Exposure = PostProcessLook.ColorGrading.Exposure;
        Temperature = PostProcessLook.Grade.Temperature;
        Tint = PostProcessLook.Grade.Tint;
        Lift = PostProcessLook.Grade.PrimaryLift;
        Gamma = PostProcessLook.Grade.PrimaryGamma;
        Gain = PostProcessLook.Grade.PrimaryGain;
        _hueSamples.Clear();
    }

    public ColorGradeQualifier Clone()
    {
        ColorGradeQualifier clone = new()
        {
            Enabled = Enabled,
            Invert = Invert,
            HueCenter = HueCenter,
            HueWidth = HueWidth,
            HueSoftness = HueSoftness,
            SaturationCenter = SaturationCenter,
            SaturationWidth = SaturationWidth,
            SaturationSoftness = SaturationSoftness,
            LuminanceCenter = LuminanceCenter,
            LuminanceWidth = LuminanceWidth,
            LuminanceSoftness = LuminanceSoftness,
            HueShift = HueShift,
            Saturation = Saturation,
            Exposure = Exposure,
            Temperature = Temperature,
            Tint = Tint,
            Lift = Lift,
            Gamma = Gamma,
            Gain = Gain,
        };
        foreach (float sample in _hueSamples)
        {
            clone.AddHueSample(sample);
        }

        return clone;
    }

    public bool ContentEquals(ColorGradeQualifier other)
    {
        if (ReferenceEquals(this, other))
        {
            return true;
        }

        if (Enabled != other.Enabled ||
            Invert != other.Invert ||
            HueCenter != other.HueCenter ||
            HueWidth != other.HueWidth ||
            HueSoftness != other.HueSoftness ||
            SaturationCenter != other.SaturationCenter ||
            SaturationWidth != other.SaturationWidth ||
            SaturationSoftness != other.SaturationSoftness ||
            LuminanceCenter != other.LuminanceCenter ||
            LuminanceWidth != other.LuminanceWidth ||
            LuminanceSoftness != other.LuminanceSoftness ||
            HueShift != other.HueShift ||
            Saturation != other.Saturation ||
            Exposure != other.Exposure ||
            Temperature != other.Temperature ||
            Tint != other.Tint ||
            Lift != other.Lift ||
            Gamma != other.Gamma ||
            Gain != other.Gain ||
            _hueSamples.Count != other._hueSamples.Count)
        {
            return false;
        }

        for (int index = 0; index < _hueSamples.Count; index++)
        {
            if (_hueSamples[index] != other._hueSamples[index])
            {
                return false;
            }
        }

        return true;
    }

    public void Sanitize()
    {
        HueCenter = FiniteClamp(HueCenter, 0f, 360f, 120f);
        HueWidth = FiniteClamp(HueWidth, 0f, 180f, 30f);
        HueSoftness = FiniteClamp(HueSoftness, 0f, 180f, 15f);
        SaturationCenter = FiniteClamp(SaturationCenter, 0f, 1f, 0.5f);
        SaturationWidth = FiniteClamp(SaturationWidth, 0f, 1f, 0.5f);
        SaturationSoftness = FiniteClamp(SaturationSoftness, 0f, 1f, 0.1f);
        LuminanceCenter = FiniteClamp(LuminanceCenter, 0f, 1f, 0.5f);
        LuminanceWidth = FiniteClamp(LuminanceWidth, 0f, 1f, 0.5f);
        LuminanceSoftness = FiniteClamp(LuminanceSoftness, 0f, 1f, 0.1f);
        HueShift = FiniteClamp(HueShift, -180f, 180f, 0f);
        Saturation = FiniteClamp(Saturation, 0f, 2f, 1f);
        Exposure = FiniteClamp(Exposure, -8f, 8f, 0f);
        Temperature = FiniteClamp(Temperature, -100f, 100f, 0f);
        Tint = FiniteClamp(Tint, -100f, 100f, 0f);
        Lift = FiniteVector(Lift, -0.5f, 0.5f, PostProcessLook.Grade.PrimaryLift);
        Gamma = FiniteVector(Gamma, 0.1f, 4f, PostProcessLook.Grade.PrimaryGamma);
        Gain = FiniteVector(Gain, 0f, 4f, PostProcessLook.Grade.PrimaryGain);
        for (int index = _hueSamples.Count - 1; index >= 0; index--)
        {
            float sample = _hueSamples[index];
            if (!float.IsFinite(sample))
            {
                _hueSamples.RemoveAt(index);
            }
            else
            {
                _hueSamples[index] = Mathf.Repeat(sample, 360f);
            }
        }

        if (_hueSamples.Count > MaxHueSamples)
        {
            _hueSamples.RemoveRange(MaxHueSamples, _hueSamples.Count - MaxHueSamples);
        }
    }

    private static float FiniteClamp(float value, float min, float max, float fallback) =>
        float.IsFinite(value) ? Mathf.Clamp(value, min, max) : fallback;

    private static Vector3 FiniteVector(Vector3 value, float min, float max, Vector3 fallback) => new(
        FiniteClamp(value.x, min, max, fallback.x),
        FiniteClamp(value.y, min, max, fallback.y),
        FiniteClamp(value.z, min, max, fallback.z));
}
