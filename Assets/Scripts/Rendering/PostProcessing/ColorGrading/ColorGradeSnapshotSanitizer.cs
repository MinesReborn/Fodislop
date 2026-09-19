#nullable enable

using UnityEngine;

namespace Kern.Rendering.PostProcessing;

internal static class ColorGradeSnapshotSanitizer
{
    // Кривые и квалификатор, совпадающие по содержимому с уже санитизированным
    // предыдущим грейдом, берутся из него, а не клонируются заново. Смешивание
    // зон меняет при движении камеры только скаляры, и без этого каждый кадр
    // в переходе давал девять новых кривых.
    public static ColorGradeSnapshot Sanitize(in ColorGradeSnapshot snapshot, in ColorGradeSnapshot defaults, ColorGradeSnapshot? previous)
    {
        return new ColorGradeSnapshot
        {
            EnabledMask = snapshot.EnabledMask & ((1 << 6) - 1),
            Transform = snapshot.Transform is DisplayTransform.None or DisplayTransform.Sdr or DisplayTransform.HdrPq1300
                ? snapshot.Transform
                : defaults.Transform,
            Exposure = FiniteClamp(
                snapshot.Exposure,
                ColorGradeState.ExposureHardMin,
                ColorGradeState.ExposureHardMax,
                defaults.Exposure),
            WhitePoint = FiniteClamp(
                snapshot.WhitePoint,
                ColorGradeState.WhitePointMin,
                ColorGradeState.WhitePointMax,
                defaults.WhitePoint),
            Contrast = FiniteClamp(
                snapshot.Contrast,
                ColorGradeState.ContrastMin,
                ColorGradeState.ContrastMax,
                defaults.Contrast),
            Pivot = FiniteClamp(snapshot.Pivot, 0.1f, 0.9f, defaults.Pivot),
            Shadows = FiniteClamp(snapshot.Shadows, -0.5f, 0.5f, defaults.Shadows),
            Highlights = FiniteClamp(snapshot.Highlights, -0.5f, 0.5f, defaults.Highlights),
            Blacks = FiniteClamp(snapshot.Blacks, -0.5f, 0.5f, defaults.Blacks),
            Whites = FiniteClamp(snapshot.Whites, -0.5f, 0.5f, defaults.Whites),
            Toe = FiniteClamp(snapshot.Toe, 0f, 1f, defaults.Toe),
            Shoulder = FiniteClamp(snapshot.Shoulder, 0f, 1f, defaults.Shoulder),
            Temperature = FiniteClamp(
                snapshot.Temperature,
                ColorGradeState.TemperatureMin,
                ColorGradeState.TemperatureMax,
                defaults.Temperature),
            Tint = FiniteClamp(
                snapshot.Tint,
                ColorGradeState.TemperatureMin,
                ColorGradeState.TemperatureMax,
                defaults.Tint),
            Slope = FiniteClamp(
                snapshot.Slope,
                ColorGradeState.SlopeMin,
                ColorGradeState.SlopeMax,
                defaults.Slope),
            Offset = FiniteClamp(
                snapshot.Offset,
                ColorGradeState.OffsetMin,
                ColorGradeState.OffsetMax,
                defaults.Offset),
            Power = FiniteClamp(
                snapshot.Power,
                ColorGradeState.PowerMin,
                ColorGradeState.PowerMax,
                defaults.Power),
            PrimaryLift = FiniteClamp(
                snapshot.PrimaryLift,
                ColorGradeState.OffsetMin,
                ColorGradeState.OffsetMax,
                defaults.PrimaryLift),
            PrimaryGamma = FiniteClamp(
                snapshot.PrimaryGamma,
                ColorGradeState.PowerMin,
                ColorGradeState.PowerMax,
                defaults.PrimaryGamma),
            PrimaryGain = FiniteClamp(
                snapshot.PrimaryGain,
                ColorGradeState.SlopeMin,
                ColorGradeState.SlopeMax,
                defaults.PrimaryGain),
            PrimaryOffset = FiniteClamp(
                snapshot.PrimaryOffset,
                ColorGradeState.OffsetMin,
                ColorGradeState.OffsetMax,
                defaults.PrimaryOffset),
            PrimaryMaster = new Vector4(
                FiniteClamp(snapshot.PrimaryMaster.x, ColorGradeState.OffsetMin, ColorGradeState.OffsetMax, 0f),
                FiniteClamp(snapshot.PrimaryMaster.y, ColorGradeState.PowerMin, ColorGradeState.PowerMax, 1f),
                FiniteClamp(snapshot.PrimaryMaster.z, ColorGradeState.SlopeMin, ColorGradeState.SlopeMax, 1f),
                FiniteClamp(snapshot.PrimaryMaster.w, ColorGradeState.OffsetMin, ColorGradeState.OffsetMax, 0f)),
            Vibrance = FiniteClamp(snapshot.Vibrance, -1f, 1f, defaults.Vibrance),
            Saturation = FiniteClamp(
                snapshot.Saturation,
                ColorGradeState.SaturationMin,
                ColorGradeState.SaturationMax,
                defaults.Saturation),
            CdlSaturation = FiniteClamp(
                snapshot.CdlSaturation,
                ColorGradeState.CdlSaturationMin,
                ColorGradeState.CdlSaturationMax,
                defaults.CdlSaturation),
            Hue = FiniteClamp(snapshot.Hue, -180f, 180f, defaults.Hue),
            CdlMaster = new Vector3(
                FiniteClamp(snapshot.CdlMaster.x, 0f, 4f, defaults.CdlMaster.x),
                FiniteClamp(snapshot.CdlMaster.y, -0.5f, 0.5f, defaults.CdlMaster.y),
                FiniteClamp(snapshot.CdlMaster.z, 0.1f, 4f, defaults.CdlMaster.z)),
            GreyOut = FiniteClamp(
                snapshot.GreyOut,
                ColorGradeState.GreyOutMin,
                ColorGradeState.GreyOutMax,
                defaults.GreyOut),
            ShoulderPower = FiniteClamp(
                snapshot.ShoulderPower,
                ColorGradeState.CurvePowerMin,
                ColorGradeState.CurvePowerMax,
                defaults.ShoulderPower),
            ToePower = FiniteClamp(
                snapshot.ToePower,
                ColorGradeState.CurvePowerMin,
                ColorGradeState.CurvePowerMax,
                defaults.ToePower),
            ToeStops = FiniteClamp(
                snapshot.ToeStops,
                ColorGradeState.ToeStopsMin,
                ColorGradeState.ToeStopsMax,
                defaults.ToeStops),
            GamutCompressionEnabled = snapshot.GamutCompressionEnabled,
            GamutCompressionStrength = FiniteClamp(
                snapshot.GamutCompressionStrength,
                ColorGradeState.GamutCompressionStrengthMin,
                ColorGradeState.GamutCompressionStrengthMax,
                defaults.GamutCompressionStrength),
            MasterCurve = SanitizeCurve(snapshot.MasterCurve, defaults.MasterCurve, previous?.MasterCurve),
            RedCurve = SanitizeCurve(snapshot.RedCurve, defaults.RedCurve, previous?.RedCurve),
            GreenCurve = SanitizeCurve(snapshot.GreenCurve, defaults.GreenCurve, previous?.GreenCurve),
            BlueCurve = SanitizeCurve(snapshot.BlueCurve, defaults.BlueCurve, previous?.BlueCurve),
            HueVsHueCurve = SanitizeCurve(snapshot.HueVsHueCurve, defaults.HueVsHueCurve, previous?.HueVsHueCurve),
            HueVsSaturationCurve = SanitizeCurve(snapshot.HueVsSaturationCurve, defaults.HueVsSaturationCurve, previous?.HueVsSaturationCurve),
            HueVsLuminanceCurve = SanitizeCurve(snapshot.HueVsLuminanceCurve, defaults.HueVsLuminanceCurve, previous?.HueVsLuminanceCurve),
            LuminanceVsSaturationCurve = SanitizeCurve(snapshot.LuminanceVsSaturationCurve, defaults.LuminanceVsSaturationCurve, previous?.LuminanceVsSaturationCurve),
            SaturationVsSaturationCurve = SanitizeCurve(snapshot.SaturationVsSaturationCurve, defaults.SaturationVsSaturationCurve, previous?.SaturationVsSaturationCurve),
            Qualifier = SanitizeQualifier(snapshot.Qualifier, defaults.Qualifier, previous?.Qualifier),
            Lut = snapshot.LutIntensity > 0.0001f ? snapshot.Lut : null,
            LutIntensity = FiniteClamp(snapshot.LutIntensity, 0f, 1f, 0f),
            LutColorSpace = snapshot.LutColorSpace is ColorGradeLutColorSpace.LinearRec709 or ColorGradeLutColorSpace.SrgbRec709
                ? snapshot.LutColorSpace
                : defaults.LutColorSpace,
        };
    }

    private static ColorGradeCurve SanitizeCurve(
        ColorGradeCurve? curve,
        ColorGradeCurve fallback,
        ColorGradeCurve? previous)
    {
        if (previous != null && curve != null && previous.ContentEquals(curve))
        {
            return previous;
        }

        ColorGradeCurve result = curve?.Clone() ?? fallback.Clone();
        result.Sanitize();
        return result;
    }

    private static ColorGradeQualifier SanitizeQualifier(
        ColorGradeQualifier? qualifier,
        ColorGradeQualifier fallback,
        ColorGradeQualifier? previous)
    {
        if (previous != null && qualifier != null && previous.ContentEquals(qualifier))
        {
            return previous;
        }

        ColorGradeQualifier result = qualifier?.Clone() ?? fallback.Clone();
        result.Sanitize();
        return result;
    }

    private static float FiniteClamp(float value, float minimum, float maximum, float fallback) =>
        float.IsNaN(value) || float.IsInfinity(value)
            ? fallback
            : Mathf.Clamp(value, minimum, maximum);

    private static Vector3 FiniteClamp(Vector3 value, float minimum, float maximum, Vector3 fallback) =>
        new(
            FiniteClamp(value.x, minimum, maximum, fallback.x),
            FiniteClamp(value.y, minimum, maximum, fallback.y),
            FiniteClamp(value.z, minimum, maximum, fallback.z));
}
