#nullable enable

using Kern.Tools.Imgui;
using UnityEngine;

namespace Kern.Rendering.PostProcessing.Workbench;

internal static class GradingLayerSpecificControlsDrawer
{
    public static void DrawExposureControls(ColorGradeState state, GradingLayerControlsDrawer drawer)
    {
        state.Exposure = drawer.Slider(
            "exposure", "стопы", state.Exposure,
            ColorGradeState.ExposureMin, ColorGradeState.ExposureMax);
    }

    public static void DrawWhiteBalanceControls(ColorGradeState state, GradingLayerControlsDrawer drawer)
    {
        if (GUILayout.Button("Eyedropper: neutral white/gray", ToolTheme.SecondaryButton))
        {
            ColorGradeScreenSampler.Arm(sample =>
            {
                float red = Mathf.Max(sample.r, 1e-4f);
                float green = Mathf.Max(sample.g, 1e-4f);
                float blue = Mathf.Max(sample.b, 1e-4f);
                state.Temperature = Mathf.Clamp((blue - red) * -180f, -100f, 100f);
                state.Tint = Mathf.Clamp(
                    (green - (red + blue) * 0.5f) * -220f,
                    -100f,
                    100f);
                drawer.RemoveNumberText("temperature");
                drawer.RemoveNumberText("tint");
            });
        }

        state.Temperature = drawer.Slider(
            "temperature", "температура", state.Temperature,
            ColorGradeState.TemperatureMin, ColorGradeState.TemperatureMax);
        state.Tint = drawer.Slider(
            "tint", "оттенок", state.Tint,
            ColorGradeState.TemperatureMin, ColorGradeState.TemperatureMax);

        GUILayout.Label("PRIMARY COLOR WHEELS", ToolTheme.SectionLabel);
        Vector3 lift = drawer.TripletSlider(
            "primary.lift", "Lift", state.PrimaryLift,
            ColorGradeState.OffsetMin, ColorGradeState.OffsetMax);
        GradingPrimaryWheelDrawer.DrawPrimaryWheel("LIFT WHEEL", ref lift, Vector3.zero, -0.5f, 0.5f, "primary.lift.wheel");
        state.PrimaryLift = lift;

        Vector3 gamma = drawer.TripletSlider(
            "primary.gamma", "Gamma", state.PrimaryGamma,
            ColorGradeState.PowerMin, ColorGradeState.PowerMax);
        GradingPrimaryWheelDrawer.DrawPrimaryWheel("GAMMA WHEEL", ref gamma, Vector3.one, 0.1f, 4f, "primary.gamma.wheel");
        state.PrimaryGamma = gamma;

        Vector3 gain = drawer.TripletSlider(
            "primary.gain", "Gain", state.PrimaryGain,
            ColorGradeState.SlopeMin, ColorGradeState.SlopeMax);
        GradingPrimaryWheelDrawer.DrawPrimaryWheel("GAIN WHEEL", ref gain, Vector3.one, 0f, 4f, "primary.gain.wheel");
        state.PrimaryGain = gain;

        state.PrimaryOffset = drawer.TripletSlider(
            "primary.offset", "Offset", state.PrimaryOffset,
            ColorGradeState.OffsetMin, ColorGradeState.OffsetMax);
        Vector4 primaryMaster = state.PrimaryMaster;
        primaryMaster.x = drawer.Slider("primary.master.lift", "  Lift master", primaryMaster.x, -0.5f, 0.5f);
        primaryMaster.y = drawer.Slider("primary.master.gamma", "  Gamma master", primaryMaster.y, 0.1f, 4f);
        primaryMaster.z = drawer.Slider("primary.master.gain", "  Gain master", primaryMaster.z, 0f, 4f);
        primaryMaster.w = drawer.Slider("primary.master.offset", "  Offset master", primaryMaster.w, -0.5f, 0.5f);
        state.PrimaryMaster = primaryMaster;
    }

    public static void DrawCdlControls(ColorGradeState state, GradingLayerControlsDrawer drawer)
    {
        state.CdlSaturation = drawer.Slider(
            "cdl.saturation",
            "Saturation",
            state.CdlSaturation,
            ColorGradeState.CdlSaturationMin,
            ColorGradeState.CdlSaturationMax);
        Vector3 slope = drawer.TripletSlider(
            "slope", "Slope (усиление)", state.Slope,
            ColorGradeState.SlopeMin, ColorGradeState.SlopeMax);
        GradingPrimaryWheelDrawer.DrawPrimaryWheel("GAIN WHEEL", ref slope, Vector3.one, 1f, 4f, "cdl.slope.wheel");
        state.Slope = slope;
        Vector3 offset = drawer.TripletSlider(
            "offset", "Offset (подъём)", state.Offset,
            ColorGradeState.OffsetMin, ColorGradeState.OffsetMax);
        GradingPrimaryWheelDrawer.DrawPrimaryWheel("LIFT WHEEL", ref offset, Vector3.zero, -0.5f, 0.5f, "cdl.offset.wheel");
        state.Offset = offset;
        Vector3 power = drawer.TripletSlider(
            "power", "Power (гамма)", state.Power,
            ColorGradeState.PowerMin, ColorGradeState.PowerMax);
        GradingPrimaryWheelDrawer.DrawPrimaryWheel("GAMMA WHEEL", ref power, Vector3.one, 0.1f, 4f, "cdl.power.wheel");
        state.Power = power;
        GUILayout.Label("MASTER / LUMA", ToolTheme.SectionLabel);
        Vector3 master = state.CdlMaster;
        master.x = drawer.Slider("master.slope", "  Slope master", master.x, 0f, 4f);
        master.y = drawer.Slider("master.offset", "  Offset master", master.y, -0.5f, 0.5f);
        master.z = drawer.Slider("master.power", "  Power master", master.z, 0.1f, 4f);
        state.CdlMaster = master;
    }

    public static void DrawSaturationControls(ColorGradeState state, GradingLayerControlsDrawer drawer, GradingCurveEditorDrawer curveDrawer)
    {
        state.Saturation = drawer.Slider(
            "saturation", "насыщенность", state.Saturation,
            ColorGradeState.SaturationMin, ColorGradeState.SaturationMax);
        state.Vibrance = drawer.Slider(
            "vibrance", "vibrance", state.Vibrance,
            ColorGradeState.VibranceMin, ColorGradeState.VibranceMax);
        state.Hue = drawer.Slider("hue", "hue shift °", state.Hue, -180f, 180f);

        GUILayout.Label("SELECTIVE CURVES", ToolTheme.SectionLabel);
        curveDrawer.DrawCurveEditor("hue-vs-hue.curve", "Hue vs Hue", state.HueVsHueCurve, drawer);
        curveDrawer.DrawCurveEditor(
            "hue-vs-saturation.curve",
            "Hue vs Saturation",
            state.HueVsSaturationCurve,
            drawer);
        curveDrawer.DrawCurveEditor(
            "hue-vs-luminance.curve",
            "Hue vs Luminance",
            state.HueVsLuminanceCurve,
            drawer);
        curveDrawer.DrawCurveEditor(
            "luminance-vs-saturation.curve",
            "Luminance vs Saturation",
            state.LuminanceVsSaturationCurve,
            drawer);
        curveDrawer.DrawCurveEditor(
            "saturation-vs-saturation.curve",
            "Saturation vs Saturation",
            state.SaturationVsSaturationCurve,
            drawer);
        GUILayout.Label(
            "Кривые «X против Y»: линия посередине — без изменений. Выше — больше, " +
            "ниже — меньше: оттенок сдвигается до ±180°, насыщенность и яркость " +
            "умножаются от ×0 до ×2. У кривых по оттенку края связаны — 0° и 360° " +
            "это один цвет; на серые и почти чёрные пиксели они не действуют.",
            ToolTheme.MutedLabel);
    }

    public static void DrawContrastControls(ColorGradeState state, GradingLayerControlsDrawer drawer)
    {
        state.Contrast = drawer.Slider(
            "contrast", "контраст", state.Contrast,
            ColorGradeState.ContrastMin, ColorGradeState.ContrastMax);
        state.Pivot = drawer.Slider("pivot", "pivot", state.Pivot, 0.1f, 0.9f);
        state.Shadows = drawer.Slider("shadows", "shadows", state.Shadows, -0.5f, 0.5f);
        state.Highlights = drawer.Slider("highlights", "highlights", state.Highlights, -0.5f, 0.5f);
        state.Blacks = drawer.Slider("blacks", "blacks", state.Blacks, -0.5f, 0.5f);
        state.Whites = drawer.Slider("whites", "whites", state.Whites, -0.5f, 0.5f);
        state.Toe = drawer.Slider("toe", "toe", state.Toe, 0f, 1f);
        state.Shoulder = drawer.Slider("shoulder", "shoulder", state.Shoulder, 0f, 1f);
    }

    public static void DrawCurveControls(ColorGradeState state, GradingLayerControlsDrawer drawer, GradingCurveEditorDrawer curveDrawer)
    {
        GUILayout.Label("КРИВЫЕ ТОНА", ToolTheme.SectionLabel);
        if (GUILayout.Button(
                state.Transform switch
                {
                    DisplayTransform.Sdr => "Display transform: SDR",
                    DisplayTransform.HdrPq1300 => "Display transform: HDR PQ 1300",
                    _ => "Display transform: None",
                },
                ToolTheme.SecondaryButton))
        {
            state.Transform = state.Transform switch
            {
                DisplayTransform.None => DisplayTransform.Sdr,
                DisplayTransform.Sdr => DisplayTransform.HdrPq1300,
                _ => DisplayTransform.None,
            };
        }

        state.WhitePoint = drawer.Slider("white-point", "white point", state.WhitePoint, 0.25f, 8f);
        state.GreyOut = drawer.Slider("grey-out", "grey output", state.GreyOut, 0.05f, 0.5f);
        state.ToePower = drawer.Slider("toe-power", "toe power", state.ToePower, 1f, 8f);
        state.ToeStops = drawer.Slider("toe-stops", "toe stops", state.ToeStops, 4f, 20f);
        state.ShoulderPower = drawer.Slider("shoulder-power", "shoulder power", state.ShoulderPower, 1f, 8f);

        state.GamutCompressionEnabled = GUILayout.Toggle(
            state.GamutCompressionEnabled,
            state.GamutCompressionEnabled ? "●  Сжатие гамута" : "○  Сжатие гамута",
            ToolTheme.SegmentedButton);
        bool gamutControlsEnabled = GUI.enabled;
        GUI.enabled = gamutControlsEnabled && state.GamutCompressionEnabled;
        state.GamutCompressionStrength = drawer.Slider(
            "gamut-compression",
            "  сила сжатия",
            state.GamutCompressionStrength,
            ColorGradeState.GamutCompressionStrengthMin,
            ColorGradeState.GamutCompressionStrengthMax);
        GUI.enabled = gamutControlsEnabled;
        curveDrawer.DrawCurveEditor("master-curve", "Master / Luma", state.MasterCurve, drawer);
        curveDrawer.DrawCurveEditor("red-curve", "Red", state.RedCurve, drawer);
        curveDrawer.DrawCurveEditor("green-curve", "Green", state.GreenCurve, drawer);
        curveDrawer.DrawCurveEditor("blue-curve", "Blue", state.BlueCurve, drawer);
        GUILayout.Label(
            "ЛКМ по полю добавляет точку, drag двигает её, ПКМ удаляет. " +
            "Крайние точки фиксированы; smooth использует ограниченный smoothstep без overshoot.",
            ToolTheme.MutedLabel);
    }
}
