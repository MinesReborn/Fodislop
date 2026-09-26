#nullable enable

using System;

namespace Kern.Rendering.PostProcessing;

public static class ColorGradeDefaults
{
    public static void ResetToLook(ColorGradeState state)
    {
        state.EnabledMask = (1 << 6) - 1;
        state.Transform = PostProcessLook.Grade.Transform;
        state.Exposure = PostProcessLook.ColorGrading.Exposure;
        state.Contrast = PostProcessLook.ColorGrading.Contrast;
        state.Pivot = PostProcessLook.ColorGrading.ContrastPivot;
        state.Shadows = PostProcessLook.ColorGrading.TonalAdjustment;
        state.Highlights = PostProcessLook.ColorGrading.TonalAdjustment;
        state.Blacks = PostProcessLook.ColorGrading.TonalAdjustment;
        state.Whites = PostProcessLook.ColorGrading.TonalAdjustment;
        state.Toe = PostProcessLook.ColorGrading.TonalAdjustment;
        state.Shoulder = PostProcessLook.ColorGrading.TonalAdjustment;
        state.Saturation = PostProcessLook.ColorGrading.Saturation;
        state.CdlSaturation = PostProcessLook.ColorGrading.CdlSaturation;
        state.Vibrance = PostProcessLook.ColorGrading.Vibrance;
        state.Hue = PostProcessLook.ColorGrading.Hue;
        state.Temperature = PostProcessLook.Grade.Temperature;
        state.Tint = PostProcessLook.Grade.Tint;
        state.Slope = PostProcessLook.Grade.Slope;
        state.Offset = PostProcessLook.Grade.Offset;
        state.Power = PostProcessLook.Grade.Power;
        state.PrimaryLift = PostProcessLook.Grade.PrimaryLift;
        state.PrimaryGamma = PostProcessLook.Grade.PrimaryGamma;
        state.PrimaryGain = PostProcessLook.Grade.PrimaryGain;
        state.PrimaryOffset = PostProcessLook.Grade.PrimaryOffset;
        state.PrimaryMaster = PostProcessLook.Grade.PrimaryMaster;
        state.CdlMaster = PostProcessLook.ColorGrading.CdlMaster;
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
                state.PrimaryLift = PostProcessLook.Grade.PrimaryLift;
                state.PrimaryGamma = PostProcessLook.Grade.PrimaryGamma;
                state.PrimaryGain = PostProcessLook.Grade.PrimaryGain;
                state.PrimaryOffset = PostProcessLook.Grade.PrimaryOffset;
                state.PrimaryMaster = PostProcessLook.Grade.PrimaryMaster;
                break;
            case ColorGradeLayer.Cdl:
                state.Slope = PostProcessLook.Grade.Slope;
                state.Offset = PostProcessLook.Grade.Offset;
                state.Power = PostProcessLook.Grade.Power;
                state.CdlSaturation = PostProcessLook.ColorGrading.CdlSaturation;
                state.CdlMaster = PostProcessLook.ColorGrading.CdlMaster;
                break;
            case ColorGradeLayer.Saturation:
                state.Saturation = PostProcessLook.ColorGrading.Saturation;
                state.Vibrance = PostProcessLook.ColorGrading.Vibrance;
                state.Hue = PostProcessLook.ColorGrading.Hue;
                state.HueVsHueCurve.Reset();
                state.HueVsSaturationCurve.Reset();
                state.HueVsLuminanceCurve.Reset();
                state.LuminanceVsSaturationCurve.Reset();
                state.SaturationVsSaturationCurve.Reset();
                break;
            case ColorGradeLayer.Contrast:
                state.Contrast = PostProcessLook.ColorGrading.Contrast;
                state.Pivot = PostProcessLook.ColorGrading.ContrastPivot;
                state.Shadows = PostProcessLook.ColorGrading.TonalAdjustment;
                state.Highlights = PostProcessLook.ColorGrading.TonalAdjustment;
                state.Blacks = PostProcessLook.ColorGrading.TonalAdjustment;
                state.Whites = PostProcessLook.ColorGrading.TonalAdjustment;
                state.Toe = PostProcessLook.ColorGrading.TonalAdjustment;
                state.Shoulder = PostProcessLook.ColorGrading.TonalAdjustment;
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
