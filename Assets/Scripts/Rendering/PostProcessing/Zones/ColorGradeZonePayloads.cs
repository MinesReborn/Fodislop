#nullable enable

using System;
using UnityEngine;

namespace Kern.Rendering.PostProcessing;
[Serializable]
internal sealed class ColorGradeZonePayload
{
    public string Name = string.Empty;
    public float CenterY;
    public float HalfHeight;
    public float Feather;
    public float Exposure = PostProcessLook.ColorGrading.Exposure;
    public float Contrast = PostProcessLook.ColorGrading.Contrast;
    public float Saturation = PostProcessLook.ColorGrading.Saturation;
    public float CdlSaturation = 1f;
    public int Transform;
    public float Temperature;
    public float Tint;
    public Vector3 Slope = Vector3.one;
    public Vector3 Offset;
    public Vector3 Power = Vector3.one;
    public Vector3 PrimaryLift;
    public Vector3 PrimaryGamma = Vector3.one;
    public Vector3 PrimaryGain = Vector3.one;
    public Vector3 PrimaryOffset;
    public Vector4 PrimaryMaster = new(0f, 1f, 1f, 0f);
    public float WhitePoint = 1f;
    public float GreyOut = PostProcessLook.Grade.GreyOut;
    public float ShoulderPower = PostProcessLook.Grade.ShoulderPower;
    public float ToePower = PostProcessLook.Grade.ToePower;
    public float ToeStops = PostProcessLook.Grade.ToeStops;
    public bool GamutCompressionEnabled = PostProcessLook.Grade.GamutCompressionEnabled;
    public float GamutCompressionStrength = PostProcessLook.Grade.GamutCompressionStrength;
}

internal static class ColorGradeZonePayloads
{
    public static ColorGradeZonePayload[] From(ColorGradeZones? zones)
    {
        if (zones == null || zones.Count == 0)
        {
            return Array.Empty<ColorGradeZonePayload>();
        }

        var result = new ColorGradeZonePayload[zones.Count];
        for (int index = 0; index < zones.Count; index++)
        {
            ColorGradeZone zone = zones.Zones[index];
            ColorGradeSnapshot grade = zone.Grade;
            result[index] = new ColorGradeZonePayload
            {
                Name = zone.Name,
                CenterY = zone.CenterY,
                HalfHeight = zone.HalfHeight,
                Feather = zone.Feather,
                Exposure = zone.Exposure,
                Contrast = zone.Contrast,
                Saturation = zone.Saturation,
                CdlSaturation = grade.CdlSaturation,
                Transform = (int)grade.Transform,
                Temperature = grade.Temperature,
                Tint = grade.Tint,
                Slope = grade.Slope,
                Offset = grade.Offset,
                Power = grade.Power,
                PrimaryLift = grade.PrimaryLift,
                PrimaryGamma = grade.PrimaryGamma,
                PrimaryGain = grade.PrimaryGain,
                PrimaryOffset = grade.PrimaryOffset,
                PrimaryMaster = grade.PrimaryMaster,
                WhitePoint = grade.WhitePoint,
                GreyOut = grade.GreyOut,
                ShoulderPower = grade.ShoulderPower,
                ToePower = grade.ToePower,
                ToeStops = grade.ToeStops,
                GamutCompressionEnabled = grade.GamutCompressionEnabled,
                GamutCompressionStrength = grade.GamutCompressionStrength,
            };
        }

        return result;
    }

    public static void Into(
        ColorGradeZones? zones,
        bool enabled,
        ColorGradeZonePayload[]? payloads)
    {
        if (zones == null)
        {
            return;
        }

        zones.Clear();
        zones.Enabled = enabled;
        if (payloads == null)
        {
            return;
        }

        foreach (ColorGradeZonePayload payload in payloads)
        {
            if (payload == null)
            {
                continue;
            }

            zones.Add(new ColorGradeZone
            {
                Name = payload.Name,
                CenterY = payload.CenterY,
                HalfHeight = payload.HalfHeight,
                Feather = payload.Feather,
                Exposure = payload.Exposure,
                Contrast = payload.Contrast,
                Saturation = payload.Saturation,
                Grade = new ColorGradeSnapshot
                {
                    Transform = (DisplayTransform)payload.Transform,
                    Exposure = payload.Exposure,
                    Temperature = payload.Temperature,
                    Tint = payload.Tint,
                    Slope = payload.Slope,
                    Offset = payload.Offset,
                    Power = payload.Power,
                    PrimaryLift = payload.PrimaryLift,
                    PrimaryGamma = payload.PrimaryGamma,
                    PrimaryGain = payload.PrimaryGain,
                    PrimaryOffset = payload.PrimaryOffset,
                    PrimaryMaster = payload.PrimaryMaster,
                    CdlSaturation = payload.CdlSaturation,
                    WhitePoint = payload.WhitePoint,
                    GreyOut = payload.GreyOut,
                    ShoulderPower = payload.ShoulderPower,
                    ToePower = payload.ToePower,
                    ToeStops = payload.ToeStops,
                    GamutCompressionEnabled = payload.GamutCompressionEnabled,
                    GamutCompressionStrength = payload.GamutCompressionStrength,
                },
            });
        }
    }
}
