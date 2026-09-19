#nullable enable

using System;
using System.Globalization;
using System.Text;
using UnityEngine;

namespace Kern.Rendering.PostProcessing;

internal static class ColorGradeExporter
{
    public static bool ExportCdl(ColorGradeState state, string cdlPath)
    {
        state.Sanitize();
        CultureInfo culture = CultureInfo.InvariantCulture;
        var builder = new StringBuilder();
        builder.AppendLine("<?xml version=\"1.0\" encoding=\"UTF-8\"?>");
        builder.AppendLine("<ColorDecisionList xmlns=\"urn:ASC:CDL:v1.01\">");
        builder.AppendLine("  <ColorDecision>");
        builder.AppendLine("    <ColorCorrection id=\"kern\">");
        builder.AppendLine("      <SOPNode>");
        builder.AppendLine($"        <Slope>{Triplet(state.Slope, culture)}</Slope>");
        builder.AppendLine($"        <Offset>{Triplet(state.Offset, culture)}</Offset>");
        builder.AppendLine($"        <Power>{Triplet(state.Power, culture)}</Power>");
        builder.AppendLine("      </SOPNode>");
        builder.AppendLine("      <SatNode>");
        builder.AppendLine(
            $"        <Saturation>{state.CdlSaturation.ToString("F6", culture)}</Saturation>");
        builder.AppendLine("      </SatNode>");
        builder.AppendLine("    </ColorCorrection>");
        builder.AppendLine("  </ColorDecision>");
        builder.AppendLine("</ColorDecisionList>");

        try
        {
            ColorGradeFile.WriteAtomically(cdlPath, builder.ToString());
            Debug.Log($"[ColorGrade] ASC CDL -> {cdlPath}");
            return true;
        }
        catch (Exception exception)
        {
            Debug.LogError($"[ColorGrade] Не удалось экспортировать {cdlPath}: {exception.Message}");
            return false;
        }
    }

    public static string ToLookSource(ColorGradeState state)
    {
        state.Sanitize();
        CultureInfo culture = CultureInfo.InvariantCulture;
        var builder = new StringBuilder();
        builder.AppendLine("    public static class ColorGrading");
        builder.AppendLine("    {");
        Constant(builder, culture, "Exposure", state.Exposure);
        Constant(builder, culture, "Contrast", state.Contrast);
        Constant(builder, culture, "Pivot", state.Pivot);
        Constant(builder, culture, "Shadows", state.Shadows);
        Constant(builder, culture, "Highlights", state.Highlights);
        Constant(builder, culture, "Blacks", state.Blacks);
        Constant(builder, culture, "Whites", state.Whites);
        Constant(builder, culture, "Toe", state.Toe);
        Constant(builder, culture, "Shoulder", state.Shoulder);
        Constant(builder, culture, "Saturation", state.Saturation);
        Constant(builder, culture, "CdlSaturation", state.CdlSaturation);
        Constant(builder, culture, "Vibrance", state.Vibrance);
        Constant(builder, culture, "Hue", state.Hue);
        AppendCurveSource(builder, culture, "HueVsHueCurve", state.HueVsHueCurve);
        AppendCurveSource(builder, culture, "HueVsSaturationCurve", state.HueVsSaturationCurve);
        AppendCurveSource(builder, culture, "HueVsLuminanceCurve", state.HueVsLuminanceCurve);
        AppendCurveSource(
            builder,
            culture,
            "LuminanceVsSaturationCurve",
            state.LuminanceVsSaturationCurve);
        AppendCurveSource(
            builder,
            culture,
            "SaturationVsSaturationCurve",
            state.SaturationVsSaturationCurve);
        builder.AppendLine();
        builder.AppendLine("        public static Color Filter => Color.white;");
        builder.AppendLine("    }");
        builder.AppendLine();
        builder.AppendLine("    public static class Grade");
        builder.AppendLine("    {");
        builder.AppendLine(
            $"        public const DisplayTransform Transform = DisplayTransform.{state.Transform};");
        Constant(builder, culture, "WhitePoint", state.WhitePoint);
        Constant(builder, culture, "Temperature", state.Temperature);
        Constant(builder, culture, "Tint", state.Tint);
        builder.AppendLine();
        builder.AppendLine($"        public static Vector3 Slope => {VectorSource(state.Slope, culture)};");
        builder.AppendLine();
        builder.AppendLine($"        public static Vector3 Offset => {VectorSource(state.Offset, culture)};");
        builder.AppendLine();
        builder.AppendLine($"        public static Vector3 Power => {VectorSource(state.Power, culture)};");
        builder.AppendLine();
        builder.AppendLine($"        public static Vector3 PrimaryLift => {VectorSource(state.PrimaryLift, culture)};");
        builder.AppendLine();
        builder.AppendLine($"        public static Vector3 PrimaryGamma => {VectorSource(state.PrimaryGamma, culture)};");
        builder.AppendLine();
        builder.AppendLine($"        public static Vector3 PrimaryGain => {VectorSource(state.PrimaryGain, culture)};");
        builder.AppendLine();
        builder.AppendLine($"        public static Vector3 PrimaryOffset => {VectorSource(state.PrimaryOffset, culture)};");
        builder.AppendLine();
        builder.AppendLine($"        public static Vector4 PrimaryMaster => {VectorSource(state.PrimaryMaster, culture)};");
        builder.AppendLine();
        builder.AppendLine($"        public static Vector3 CdlMaster => {VectorSource(state.CdlMaster, culture)};");
        builder.AppendLine();
        Constant(builder, culture, "GreyOut", state.GreyOut);
        Constant(builder, culture, "ShoulderPower", state.ShoulderPower);
        Constant(builder, culture, "ToePower", state.ToePower);
        Constant(builder, culture, "ToeStops", state.ToeStops);
        builder.AppendLine("    }");
        return builder.ToString();
    }

    private static string Triplet(Vector3 value, CultureInfo culture) =>
        string.Concat(
            value.x.ToString("F6", culture), " ",
            value.y.ToString("F6", culture), " ",
            value.z.ToString("F6", culture));

    private static void Constant(StringBuilder builder, CultureInfo culture, string name, float value) =>
        builder.AppendLine($"        public const float {name} = {value.ToString("0.######", culture)}f;");

    private static string VectorSource(Vector3 value, CultureInfo culture) =>
        string.Concat(
            "new(", value.x.ToString("0.######", culture), "f, ",
            value.y.ToString("0.######", culture), "f, ",
            value.z.ToString("0.######", culture), "f)");

    private static string VectorSource(Vector4 value, CultureInfo culture) =>
        string.Concat(
            "new(", value.x.ToString("0.######", culture), "f, ",
            value.y.ToString("0.######", culture), "f, ",
            value.z.ToString("0.######", culture), "f, ",
            value.w.ToString("0.######", culture), "f)");

    private static void AppendCurveSource(
        StringBuilder builder,
        CultureInfo culture,
        string name,
        ColorGradeCurve curve)
    {
        builder.AppendLine($"        public static Vector2[] {name} => new Vector2[]");
        builder.AppendLine("        {");
        for (int index = 0; index < curve.PointCount; index++)
        {
            Vector2 point = curve.GetPoint(index);
            string x = point.x.ToString("F6", culture);
            string y = point.y.ToString("F6", culture);
            builder.AppendLine(
                $"            new Vector2({x}f, {y}f),");
        }

        builder.AppendLine("        };");
    }
}
