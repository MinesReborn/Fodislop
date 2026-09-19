#nullable enable

using UnityEngine;

namespace Kern.Rendering.PostProcessing;

public static class ColorGradeSanitizer
{
    private const int LayerCount = 6;

    public static void Sanitize(ColorGradeState state)
    {
        state.EnabledMask &= (1 << LayerCount) - 1;
        if (state.Transform is not (DisplayTransform.None or DisplayTransform.Sdr or DisplayTransform.HdrPq1300))
        {
            state.Transform = PostProcessLook.Grade.Transform;
        }

        state.Exposure = FiniteClamp(
            state.Exposure,
            ColorGradeState.ExposureHardMin,
            ColorGradeState.ExposureHardMax,
            PostProcessLook.ColorGrading.Exposure);
        state.Contrast = FiniteClamp(state.Contrast, ColorGradeState.ContrastMin, ColorGradeState.ContrastMax, PostProcessLook.ColorGrading.Contrast);
        state.Pivot = FiniteClamp(state.Pivot, ColorGradeState.PivotMin, ColorGradeState.PivotMax, 0.5f);
        state.Shadows = FiniteClamp(state.Shadows, ColorGradeState.TonalAdjustmentMin, ColorGradeState.TonalAdjustmentMax, 0f);
        state.Highlights = FiniteClamp(state.Highlights, ColorGradeState.TonalAdjustmentMin, ColorGradeState.TonalAdjustmentMax, 0f);
        state.Blacks = FiniteClamp(state.Blacks, ColorGradeState.TonalAdjustmentMin, ColorGradeState.TonalAdjustmentMax, 0f);
        state.Whites = FiniteClamp(state.Whites, ColorGradeState.TonalAdjustmentMin, ColorGradeState.TonalAdjustmentMax, 0f);
        state.Toe = FiniteClamp(state.Toe, 0f, 1f, 0f);
        state.Shoulder = FiniteClamp(state.Shoulder, 0f, 1f, 0f);
        state.Saturation = FiniteClamp(state.Saturation, ColorGradeState.SaturationMin, ColorGradeState.SaturationMax, PostProcessLook.ColorGrading.Saturation);
        state.CdlSaturation = FiniteClamp(state.CdlSaturation, ColorGradeState.CdlSaturationMin, ColorGradeState.CdlSaturationMax, 1f);
        state.Vibrance = FiniteClamp(state.Vibrance, ColorGradeState.VibranceMin, ColorGradeState.VibranceMax, 0f);
        state.Hue = FiniteClamp(state.Hue, ColorGradeState.HueMin, ColorGradeState.HueMax, 0f);
        state.Temperature = FiniteClamp(state.Temperature, ColorGradeState.TemperatureMin, ColorGradeState.TemperatureMax, PostProcessLook.Grade.Temperature);
        state.Tint = FiniteClamp(state.Tint, ColorGradeState.TemperatureMin, ColorGradeState.TemperatureMax, PostProcessLook.Grade.Tint);
        state.Slope = FiniteClamp(state.Slope, ColorGradeState.SlopeMin, ColorGradeState.SlopeMax, PostProcessLook.Grade.Slope);
        state.Offset = FiniteClamp(state.Offset, ColorGradeState.OffsetMin, ColorGradeState.OffsetMax, PostProcessLook.Grade.Offset);
        state.Power = FiniteClamp(state.Power, ColorGradeState.PowerMin, ColorGradeState.PowerMax, PostProcessLook.Grade.Power);
        state.PrimaryLift = FiniteClamp(state.PrimaryLift, ColorGradeState.OffsetMin, ColorGradeState.OffsetMax, Vector3.zero);
        state.PrimaryGamma = FiniteClamp(state.PrimaryGamma, ColorGradeState.PowerMin, ColorGradeState.PowerMax, Vector3.one);
        state.PrimaryGain = FiniteClamp(state.PrimaryGain, ColorGradeState.SlopeMin, ColorGradeState.SlopeMax, Vector3.one);
        state.PrimaryOffset = FiniteClamp(state.PrimaryOffset, ColorGradeState.OffsetMin, ColorGradeState.OffsetMax, Vector3.zero);
        state.PrimaryMaster = new Vector4(
            FiniteClamp(state.PrimaryMaster.x, ColorGradeState.OffsetMin, ColorGradeState.OffsetMax, 0f),
            FiniteClamp(state.PrimaryMaster.y, ColorGradeState.PowerMin, ColorGradeState.PowerMax, 1f),
            FiniteClamp(state.PrimaryMaster.z, ColorGradeState.SlopeMin, ColorGradeState.SlopeMax, 1f),
            FiniteClamp(state.PrimaryMaster.w, ColorGradeState.OffsetMin, ColorGradeState.OffsetMax, 0f));
        state.CdlMaster = new Vector3(
            FiniteClamp(state.CdlMaster.x, ColorGradeState.SlopeMin, ColorGradeState.SlopeMax, 1f),
            FiniteClamp(state.CdlMaster.y, ColorGradeState.OffsetMin, ColorGradeState.OffsetMax, 0f),
            FiniteClamp(state.CdlMaster.z, ColorGradeState.PowerMin, ColorGradeState.PowerMax, 1f));
        state.WhitePoint = FiniteClamp(state.WhitePoint, ColorGradeState.WhitePointMin, ColorGradeState.WhitePointMax, PostProcessLook.Grade.WhitePoint);
        state.GreyOut = FiniteClamp(state.GreyOut, ColorGradeState.GreyOutMin, ColorGradeState.GreyOutMax, PostProcessLook.Grade.GreyOut);
        state.ShoulderPower = FiniteClamp(
            state.ShoulderPower,
            ColorGradeState.CurvePowerMin,
            ColorGradeState.CurvePowerMax,
            PostProcessLook.Grade.ShoulderPower);
        state.ToePower = FiniteClamp(state.ToePower, ColorGradeState.CurvePowerMin, ColorGradeState.CurvePowerMax, PostProcessLook.Grade.ToePower);
        state.ToeStops = FiniteClamp(state.ToeStops, ColorGradeState.ToeStopsMin, ColorGradeState.ToeStopsMax, PostProcessLook.Grade.ToeStops);
        state.GamutCompressionStrength = FiniteClamp(
            state.GamutCompressionStrength,
            ColorGradeState.GamutCompressionStrengthMin,
            ColorGradeState.GamutCompressionStrengthMax,
            PostProcessLook.Grade.GamutCompressionStrength);
        state.MasterCurve.Sanitize();
        state.RedCurve.Sanitize();
        state.GreenCurve.Sanitize();
        state.BlueCurve.Sanitize();
        state.HueVsHueCurve.Sanitize();
        state.HueVsSaturationCurve.Sanitize();
        state.HueVsLuminanceCurve.Sanitize();
        state.LuminanceVsSaturationCurve.Sanitize();
        state.SaturationVsSaturationCurve.Sanitize();
        state.Qualifier.Sanitize();
        state.LutIntensity = FiniteClamp(state.LutIntensity, 0f, 1f, 0f);
        if (state.LutColorSpace is not (ColorGradeLutColorSpace.LinearRec709 or ColorGradeLutColorSpace.SrgbRec709))
        {
            state.LutColorSpace = ColorGradeLutColorSpace.LinearRec709;
        }

        // Диапазон, а не Enum.IsDefined: Sanitize идёт каждый кадр, пока
        // рабочее место применяет грейд, а IsDefined упаковывает и ходит в reflection.
        if (state.Solo.HasValue && (uint)(int)state.Solo.Value >= LayerCount)
        {
            state.Solo = null;
        }

        if (state.Solo.HasValue && !state.IsEnabled(state.Solo.Value))
        {
            state.Solo = null;
        }
    }

    public static float FiniteClamp(float value, float minimum, float maximum, float fallback) =>
        float.IsNaN(value) || float.IsInfinity(value)
            ? fallback
            : Mathf.Clamp(value, minimum, maximum);

    public static Vector3 FiniteClamp(Vector3 value, float minimum, float maximum, Vector3 fallback) =>
        new(
            FiniteClamp(value.x, minimum, maximum, fallback.x),
            FiniteClamp(value.y, minimum, maximum, fallback.y),
            FiniteClamp(value.z, minimum, maximum, fallback.z));
}
