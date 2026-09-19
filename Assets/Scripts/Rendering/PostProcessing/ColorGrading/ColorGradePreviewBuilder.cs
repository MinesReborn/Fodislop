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
            Exposure = state.IsActive(ColorGradeLayer.Exposure) ? authored.Exposure : 0f,
            CdlSaturation = state.IsActive(ColorGradeLayer.Cdl) ? authored.CdlSaturation : 1f,
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
            Temperature = state.IsActive(ColorGradeLayer.WhiteBalance) ? authored.Temperature : 0f,
            Tint = state.IsActive(ColorGradeLayer.WhiteBalance) ? authored.Tint : 0f,
            PrimaryLift = state.IsActive(ColorGradeLayer.WhiteBalance)
                ? authored.PrimaryLift
                : Vector3.zero,
            PrimaryGamma = state.IsActive(ColorGradeLayer.WhiteBalance)
                ? authored.PrimaryGamma
                : Vector3.one,
            PrimaryGain = state.IsActive(ColorGradeLayer.WhiteBalance)
                ? authored.PrimaryGain
                : Vector3.one,
            PrimaryOffset = state.IsActive(ColorGradeLayer.WhiteBalance)
                ? authored.PrimaryOffset
                : Vector3.zero,
            PrimaryMaster = state.IsActive(ColorGradeLayer.WhiteBalance)
                ? authored.PrimaryMaster
                : new Vector4(0f, 1f, 1f, 0f),
            Slope = state.IsActive(ColorGradeLayer.Cdl) ? authored.Slope : Vector3.one,
            Offset = state.IsActive(ColorGradeLayer.Cdl) ? authored.Offset : Vector3.zero,
            Power = state.IsActive(ColorGradeLayer.Cdl) ? authored.Power : Vector3.one,
            CdlMaster = state.IsActive(ColorGradeLayer.Cdl) ? authored.CdlMaster : new Vector3(1f, 0f, 1f),
            Pivot = state.IsActive(ColorGradeLayer.Contrast) ? authored.Pivot : 0.5f,
            Shadows = state.IsActive(ColorGradeLayer.Contrast) ? authored.Shadows : 0f,
            Highlights = state.IsActive(ColorGradeLayer.Contrast) ? authored.Highlights : 0f,
            Blacks = state.IsActive(ColorGradeLayer.Contrast) ? authored.Blacks : 0f,
            Whites = state.IsActive(ColorGradeLayer.Contrast) ? authored.Whites : 0f,
            Toe = state.IsActive(ColorGradeLayer.Contrast) ? authored.Toe : 0f,
            Shoulder = state.IsActive(ColorGradeLayer.Contrast) ? authored.Shoulder : 0f,
            Vibrance = state.IsActive(ColorGradeLayer.Saturation) ? authored.Vibrance : 0f,
            Hue = state.IsActive(ColorGradeLayer.Saturation) ? authored.Hue : 0f,
        };
    }
}
