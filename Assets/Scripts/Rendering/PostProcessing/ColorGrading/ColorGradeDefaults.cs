#nullable enable

using System;
using UnityEngine;

namespace Kern.Rendering.PostProcessing;

public static class ColorGradeDefaults
{
    public static void ResetToLook(ColorGradeState state)
    {
        state.EnabledMask = (1 << 6) - 1;
        state.Transform = PostProcessLook.Grade.Transform;
        state.Exposure = PostProcessLook.ColorGrading.Exposure;
        state.Contrast = PostProcessLook.ColorGrading.Contrast;
        state.Pivot = 0.5f;
        state.Shadows = 0f;
        state.Highlights = 0f;
        state.Blacks = 0f;
        state.Whites = 0f;
        state.Toe = 0f;
        state.Shoulder = 0f;
        state.Saturation = PostProcessLook.ColorGrading.Saturation;
        state.CdlSaturation = 1f;
        state.Vibrance = 0f;
        state.Hue = 0f;
        state.Temperature = PostProcessLook.Grade.Temperature;
        state.Tint = PostProcessLook.Grade.Tint;
        state.Slope = PostProcessLook.Grade.Slope;
        state.Offset = PostProcessLook.Grade.Offset;
        state.Power = PostProcessLook.Grade.Power;
        state.PrimaryLift = Vector3.zero;
        state.PrimaryGamma = Vector3.one;
        state.PrimaryGain = Vector3.one;
        state.PrimaryOffset = Vector3.zero;
        state.PrimaryMaster = new Vector4(0f, 1f, 1f, 0f);
        state.CdlMaster = new Vector3(1f, 0f, 1f);
        state.WhitePoint = PostProcessLook.Grade.WhitePoint;
        state.GreyOut = PostProcessLook.Grade.GreyOut;
        state.ShoulderPower = PostProcessLook.Grade.ShoulderPower;
        state.ToePower = PostProcessLook.Grade.ToePower;
        state.ToeStops = PostProcessLook.Grade.ToeStops;
        state.GamutCompressionEnabled = PostProcessLook.Grade.GamutCompressionEnabled;
        state.GamutCompressionStrength = PostProcessLook.Grade.GamutCompressionStrength;
        state.MasterCurve.Reset();
        state.RedCurve.Reset();
        state.GreenCurve.Reset();
        state.BlueCurve.Reset();
        state.HueVsHueCurve.Reset();
        state.HueVsSaturationCurve.Reset();
        state.HueVsLuminanceCurve.Reset();
        state.LuminanceVsSaturationCurve.Reset();
        state.SaturationVsSaturationCurve.Reset();
        state.Qualifier.Reset();
        state.ClearLut();
        state.ClearPreviewOverrides();
    }

    public static void ResetLayer(ColorGradeState state, ColorGradeLayer layer)
    {
        state.SetEnabled(layer, true);
        switch (layer)
        {
            case ColorGradeLayer.Exposure:
                state.Exposure = PostProcessLook.ColorGrading.Exposure;
                break;
            case ColorGradeLayer.WhiteBalance:
                state.Temperature = PostProcessLook.Grade.Temperature;
                state.Tint = PostProcessLook.Grade.Tint;
                state.PrimaryLift = Vector3.zero;
                state.PrimaryGamma = Vector3.one;
                state.PrimaryGain = Vector3.one;
                state.PrimaryOffset = Vector3.zero;
                state.PrimaryMaster = new Vector4(0f, 1f, 1f, 0f);
                break;
            case ColorGradeLayer.Cdl:
                state.Slope = PostProcessLook.Grade.Slope;
                state.Offset = PostProcessLook.Grade.Offset;
                state.Power = PostProcessLook.Grade.Power;
                state.CdlSaturation = 1f;
                state.CdlMaster = new Vector3(1f, 0f, 1f);
                break;
            case ColorGradeLayer.Saturation:
                state.Saturation = PostProcessLook.ColorGrading.Saturation;
                state.Vibrance = 0f;
                state.Hue = 0f;
                state.HueVsHueCurve.Reset();
                state.HueVsSaturationCurve.Reset();
                state.HueVsLuminanceCurve.Reset();
                state.LuminanceVsSaturationCurve.Reset();
                state.SaturationVsSaturationCurve.Reset();
                break;
            case ColorGradeLayer.Contrast:
                state.Contrast = PostProcessLook.ColorGrading.Contrast;
                state.Pivot = 0.5f;
                state.Shadows = 0f;
                state.Highlights = 0f;
                state.Blacks = 0f;
                state.Whites = 0f;
                state.Toe = 0f;
                state.Shoulder = 0f;
                break;
            case ColorGradeLayer.Curve:
                state.Transform = PostProcessLook.Grade.Transform;
                state.WhitePoint = PostProcessLook.Grade.WhitePoint;
                state.GreyOut = PostProcessLook.Grade.GreyOut;
                state.ShoulderPower = PostProcessLook.Grade.ShoulderPower;
                state.ToePower = PostProcessLook.Grade.ToePower;
                state.ToeStops = PostProcessLook.Grade.ToeStops;
                state.GamutCompressionEnabled = PostProcessLook.Grade.GamutCompressionEnabled;
                state.GamutCompressionStrength = PostProcessLook.Grade.GamutCompressionStrength;
                state.MasterCurve.Reset();
                state.RedCurve.Reset();
                state.GreenCurve.Reset();
                state.BlueCurve.Reset();
                state.Qualifier.Reset();
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(layer), layer, null);
        }
    }
}
