#nullable enable

using UnityEngine;

namespace Kern.Rendering.PostProcessing;

public static class ColorGradePreviewBuilder
{
    public static ColorGradeSnapshot BuildPreviewSnapshot(ColorGradeState state, in ColorGradeSnapshot authored)
    {
        return authored with
        {
            Transform = state.IsActive(ColorGradeLayer.Curve)
                ? authored.Transform
                : DisplayTransform.None,
            Exposure = state.IsActive(ColorGradeLayer.Exposure)
                ? authored.Exposure
                : PostProcessLook.ColorGrading.Exposure,
            CdlSaturation = state.IsActive(ColorGradeLayer.Cdl)
                ? authored.CdlSaturation
                : PostProcessLook.ColorGrading.CdlSaturation,
            MasterCurve = state.IsActive(ColorGradeLayer.Curve)
                ? authored.MasterCurve
                : new ColorGradeCurve(),
            RedCurve = state.IsActive(ColorGradeLayer.Curve)
                ? authored.RedCurve
                : new ColorGradeCurve(),
            GreenCurve = state.IsActive(ColorGradeLayer.Curve)
                ? authored.GreenCurve
                : new ColorGradeCurve(),
            BlueCurve = state.IsActive(ColorGradeLayer.Curve)
                ? authored.BlueCurve
                : new ColorGradeCurve(),
            HueVsHueCurve = state.IsActive(ColorGradeLayer.Saturation)
                ? authored.HueVsHueCurve
                : new ColorGradeCurve(ColorGradeCurveKind.Hue),
            HueVsSaturationCurve = state.IsActive(ColorGradeLayer.Saturation)
                ? authored.HueVsSaturationCurve
                : new ColorGradeCurve(ColorGradeCurveKind.Hue),
            HueVsLuminanceCurve = state.IsActive(ColorGradeLayer.Saturation)
                ? authored.HueVsLuminanceCurve
                : new ColorGradeCurve(ColorGradeCurveKind.Hue),
            LuminanceVsSaturationCurve = state.IsActive(ColorGradeLayer.Saturation)
                ? authored.LuminanceVsSaturationCurve
                : new ColorGradeCurve(ColorGradeCurveKind.Range),
            SaturationVsSaturationCurve = state.IsActive(ColorGradeLayer.Saturation)
                ? authored.SaturationVsSaturationCurve
                : new ColorGradeCurve(ColorGradeCurveKind.Range),
            Temperature = state.IsActive(ColorGradeLayer.WhiteBalance)
                ? authored.Temperature
                : PostProcessLook.Grade.Temperature,
            Tint = state.IsActive(ColorGradeLayer.WhiteBalance)
                ? authored.Tint
                : PostProcessLook.Grade.Tint,
            PrimaryLift = state.IsActive(ColorGradeLayer.WhiteBalance)
                ? authored.PrimaryLift
                : PostProcessLook.Grade.PrimaryLift,
            PrimaryGamma = state.IsActive(ColorGradeLayer.WhiteBalance)
                ? authored.PrimaryGamma
                : PostProcessLook.Grade.PrimaryGamma,
            PrimaryGain = state.IsActive(ColorGradeLayer.WhiteBalance)
                ? authored.PrimaryGain
                : PostProcessLook.Grade.PrimaryGain,
            PrimaryOffset = state.IsActive(ColorGradeLayer.WhiteBalance)
                ? authored.PrimaryOffset
                : PostProcessLook.Grade.PrimaryOffset,
            PrimaryMaster = state.IsActive(ColorGradeLayer.WhiteBalance)
                ? authored.PrimaryMaster
                : PostProcessLook.Grade.PrimaryMaster,
            Slope = state.IsActive(ColorGradeLayer.Cdl) ? authored.Slope : PostProcessLook.Grade.Slope,
            Offset = state.IsActive(ColorGradeLayer.Cdl) ? authored.Offset : PostProcessLook.Grade.Offset,
            Power = state.IsActive(ColorGradeLayer.Cdl) ? authored.Power : PostProcessLook.Grade.Power,
            CdlMaster = state.IsActive(ColorGradeLayer.Cdl)
                ? authored.CdlMaster
                : PostProcessLook.ColorGrading.CdlMaster,
            Pivot = state.IsActive(ColorGradeLayer.Contrast)
                ? authored.Pivot
                : PostProcessLook.ColorGrading.ContrastPivot,
            Shadows = state.IsActive(ColorGradeLayer.Contrast)
                ? authored.Shadows
                : PostProcessLook.ColorGrading.TonalAdjustment,
            Highlights = state.IsActive(ColorGradeLayer.Contrast)
                ? authored.Highlights
                : PostProcessLook.ColorGrading.TonalAdjustment,
            Blacks = state.IsActive(ColorGradeLayer.Contrast)
                ? authored.Blacks
                : PostProcessLook.ColorGrading.TonalAdjustment,
            Whites = state.IsActive(ColorGradeLayer.Contrast)
                ? authored.Whites
                : PostProcessLook.ColorGrading.TonalAdjustment,
            Toe = state.IsActive(ColorGradeLayer.Contrast)
                ? authored.Toe
                : PostProcessLook.ColorGrading.TonalAdjustment,
            Shoulder = state.IsActive(ColorGradeLayer.Contrast)
                ? authored.Shoulder
                : PostProcessLook.ColorGrading.TonalAdjustment,
            Vibrance = state.IsActive(ColorGradeLayer.Saturation)
                ? authored.Vibrance
                : PostProcessLook.ColorGrading.Vibrance,
            Hue = state.IsActive(ColorGradeLayer.Saturation)
                ? authored.Hue
                : PostProcessLook.ColorGrading.Hue,
        };
    }
}
