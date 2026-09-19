#nullable enable

using System;
using System.Collections.Generic;
using System.Globalization;
using Kern.Tools.Imgui;
using UnityEngine;

namespace Kern.Rendering.PostProcessing.Workbench;

internal sealed class GradingQualifierWindow : ToolWindow
{
    private readonly ColorGradeState _state;
    private readonly ColorGradeQualifier _qualifier;
    private string _lutPath = string.Empty;
    private Vector2 _scroll;
    private readonly Dictionary<string, string> _numberText = [];

    // Подписи пересобираются при смене того, что они показывают, а не на
    // каждое событие IMGUI.
    private readonly Dictionary<string, string[]> _channelLabels = [];
    private int _hueSampleLabelCount = -1;
    private string _hueSampleLabel = string.Empty;
    private object? _lutLabelSource;
    private string _lutLabel = NoLutLabel;
    private const string NoLutLabel = "Lut не загружен.";

    public GradingQualifierWindow(ColorGradeState state)
        : base("Qualifier / Secondary", new Rect(16f, 16f, 410f, 660f))
    {
        _state = state;
        _qualifier = state.Qualifier;
        Visible = false;
    }

    public override bool WantsSampling => false;

    public override Vector2 MinimumSize => new(360f, 420f);

    protected override void OnPlaySessionReset()
    {
        ColorGradeScreenSampler.Cancel();
        _scroll = default;
        _lutPath = _state.LutPath;
        _numberText.Clear();
        _hueSampleLabelCount = -1;
        _lutLabelSource = null;
        _lutLabel = NoLutLabel;
    }

    protected override void OnDispose()
    {
        ColorGradeScreenSampler.Cancel();
    }

    protected override void DrawContent()
    {
        // This window edits the same authored look as the layer window. Keep
        // qualifier and Lut edits undoable even when the layer window is hidden.
        _state.BeginHistoryFrame();

        using (ToolLayout.ScrollView(ref _scroll))
        {
            _qualifier.Enabled = GUILayout.Toggle(
                _qualifier.Enabled,
                "●  Включить qualifier",
                SegmentedButtonStyle);
            _qualifier.Invert = GUILayout.Toggle(
                _qualifier.Invert,
                "Invert matte",
                ToolTheme.SegmentedButton);

            GUILayout.Label("HUE RANGE", SectionLabelStyle);
            _qualifier.HueCenter = Slider("hue center", _qualifier.HueCenter, 0f, 360f);
            _qualifier.HueWidth = Slider("hue width", _qualifier.HueWidth, 0f, 180f);
            _qualifier.HueSoftness = Slider("hue softness", _qualifier.HueSoftness, 0f, 180f);
            using (ToolLayout.Horizontal())
            {
                if (GUILayout.Button("Eyedropper sample", ToolTheme.SecondaryButton))
                {
                    ColorGradeScreenSampler.Arm(sample =>
                    {
                        Color.RGBToHSV(sample, out float hue, out float saturation, out _);
                        _qualifier.HueCenter = hue * 360f;
                        _qualifier.SaturationCenter = saturation;
                        _qualifier.LuminanceCenter = Mathf.Clamp01(
                            sample.r * 0.2126f +
                            sample.g * 0.7152f +
                            sample.b * 0.0722f);
                        _numberText.Remove("hue center");
                        _numberText.Remove("sat center");
                        _numberText.Remove("luma center");
                    });
                }

                if (ColorGradeScreenSampler.IsArmed &&
                    GUILayout.Button("Cancel", ToolTheme.DangerButton))
                {
                    ColorGradeScreenSampler.Cancel();
                }

                if (GUILayout.Button("Add sample", ToolTheme.SecondaryButton))
                {
                    _qualifier.AddHueSample(_qualifier.HueCenter);
                }

                if (GUILayout.Button("Remove sample", ToolTheme.DangerButton))
                {
                    _qualifier.RemoveLastHueSample();
                }
            }

            GUILayout.Label(HueSampleLabel(), ToolTheme.MutedLabel);

            GUILayout.Label("SATURATION RANGE", SectionLabelStyle);
            _qualifier.SaturationCenter = Slider("sat center", _qualifier.SaturationCenter, 0f, 1f);
            _qualifier.SaturationWidth = Slider("sat width", _qualifier.SaturationWidth, 0f, 1f);
            _qualifier.SaturationSoftness = Slider("sat softness", _qualifier.SaturationSoftness, 0f, 1f);

            GUILayout.Label("LUMINANCE RANGE", SectionLabelStyle);
            _qualifier.LuminanceCenter = Slider("luma center", _qualifier.LuminanceCenter, 0f, 1f);
            _qualifier.LuminanceWidth = Slider("luma width", _qualifier.LuminanceWidth, 0f, 1f);
            _qualifier.LuminanceSoftness = Slider("luma softness", _qualifier.LuminanceSoftness, 0f, 1f);

            GUILayout.Label("LOCAL CORRECTION", SectionLabelStyle);
            _qualifier.HueShift = Slider("hue shift", _qualifier.HueShift, -180f, 180f);
            _qualifier.Saturation = Slider("saturation", _qualifier.Saturation, 0f, 2f);
            _qualifier.Exposure = Slider("exposure EV", _qualifier.Exposure, -8f, 8f);
            _qualifier.Temperature = Slider("temperature", _qualifier.Temperature, -100f, 100f);
            _qualifier.Tint = Slider("tint", _qualifier.Tint, -100f, 100f);
            _qualifier.Lift = Triplet("lift", _qualifier.Lift, -0.5f, 0.5f);
            _qualifier.Gamma = Triplet("gamma", _qualifier.Gamma, 0.1f, 4f);
            _qualifier.Gain = Triplet("gain", _qualifier.Gain, 0f, 4f);

            if (GUILayout.Button("Reset qualifier", ToolTheme.DangerButton))
            {
                _qualifier.Reset();
                _numberText.Clear();
            }

            GUILayout.Label("Lut", SectionLabelStyle);
            _lutPath = GUILayout.TextField(_lutPath);
            using (ToolLayout.Horizontal())
            {
                if (GUILayout.Button("Load .cube", ToolTheme.SecondaryButton))
                {
                    if (!_state.LoadLut(_lutPath, out string error))
                    {
                        Debug.LogWarning($"[ColorGrade] Lut не загружен: {error}");
                    }
                    else
                    {
                        _numberText.Remove("Lut intensity");
                    }
                }

                if (GUILayout.Button("Clear Lut", ToolTheme.DangerButton))
                {
                    _state.ClearLut();
                    _lutPath = string.Empty;
                    _numberText.Remove("Lut intensity");
                }
            }

            _state.LutIntensity = Slider("Lut intensity", _state.LutIntensity, 0f, 1f);
            GUILayout.Label(LutLabel(), ToolTheme.MutedLabel);
            GUILayout.Label("Lut input color space", ToolTheme.FieldLabel);
            bool srgb = GUILayout.Toggle(
                _state.LutColorSpace == ColorGradeLutColorSpace.SrgbRec709,
                "sRGB Rec.709 (off = Linear Rec.709)",
                ToolTheme.SegmentedButton);
            _state.LutColorSpace = srgb
                ? ColorGradeLutColorSpace.SrgbRec709
                : ColorGradeLutColorSpace.LinearRec709;

            GUILayout.Label(
                "Маска вычисляется в grading space; hue range корректно " +
                "пересекает 0°/360°. Multi-sample можно расширить кнопкой Add sample.",
                ToolTheme.MutedLabel);
        }

        _state.CommitHistoryFrame();
    }

    // GUILayoutOption — класс: GUILayout.Width в каждом слайдере в каждом
    // событии IMGUI был постоянным мусором.
    private static readonly GUILayoutOption _FieldLabelWidth = GUILayout.Width(120f);
    private static readonly GUILayoutOption _FieldWidth = GUILayout.Width(64f);

    private float Slider(string label, float value, float min, float max)
    {
        if (!_numberText.TryGetValue(label, out string? text))
        {
            text = value.ToString("0.###", CultureInfo.InvariantCulture);
            _numberText[label] = text;
        }

        using (ToolLayout.Horizontal())
        {
            GUILayout.Label(label, ToolTheme.FieldLabel, _FieldLabelWidth);
            float sliderMin = min;
            float sliderMax = max;
            if (Event.current.shift)
            {
                float fineRange = (max - min) * 0.1f;
                sliderMin = Mathf.Max(min, value - fineRange);
                sliderMax = Mathf.Min(max, value + fineRange);
            }

            float result = GUILayout.HorizontalSlider(value, sliderMin, sliderMax);
            if (!Mathf.Approximately(result, value))
            {
                text = result.ToString("0.###", CultureInfo.InvariantCulture);
                _numberText[label] = text;
            }

            string edited = GUILayout.TextField(text, _FieldWidth);
            if (edited != text)
            {
                edited = edited.Replace(',', '.');
                _numberText[label] = edited;
                if (float.TryParse(
                        edited,
                        NumberStyles.Float,
                        CultureInfo.InvariantCulture,
                        out float parsed) &&
                    float.IsFinite(parsed))
                {
                    result = Mathf.Clamp(parsed, min, max);
                }
            }

            // Источник истины — значение. Раньше текст перетирал значение на
            // каждом событии, и всё, что меняло квалификатор в обход поля
            // (пипетка, сброс, загрузка пресета), тут же откатывалось. Пока
            // какое-то поле в фокусе, текст не трогаем: там его набирают.
            if (GUIUtility.keyboardControl == 0 &&
                edited == text &&
                float.TryParse(
                    _numberText[label],
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out float committed) &&
                float.IsFinite(committed) &&
                Mathf.Abs(committed - result) > 0.0005f)
            {
                _numberText[label] = result.ToString("0.###", CultureInfo.InvariantCulture);
            }

            if (Event.current.type == EventType.MouseDown &&
                Event.current.button == 0 &&
                Event.current.clickCount == 2 &&
                GUILayoutUtility.GetLastRect().Contains(Event.current.mousePosition))
            {
                result = NeutralValue(label, min, max);
                _numberText[label] = result.ToString("0.###", CultureInfo.InvariantCulture);
                Event.current.Use();
            }

            return result;
        }
    }

    private static float NeutralValue(string label, float min, float max)
    {
        float neutral = label switch
        {
            "hue center" => 120f,
            "hue width" => 30f,
            "hue softness" => 15f,
            "sat center" or "sat width" => 0.5f,
            "sat softness" => 0.1f,
            "luma center" or "luma width" => 0.5f,
            "luma softness" => 0.1f,
            "saturation" => 1f,
            "Lut intensity" => 0f,
            _ when label.Contains("gamma", StringComparison.OrdinalIgnoreCase) => 1f,
            _ when label.Contains("gain", StringComparison.OrdinalIgnoreCase) => 1f,
            _ => 0f,
        };

        return Mathf.Clamp(neutral, min, max);
    }

    private Vector3 Triplet(string label, Vector3 value, float min, float max)
    {
        GUILayout.Label(label, ToolTheme.FieldLabel);
        if (!_channelLabels.TryGetValue(label, out string[]? labels))
        {
            labels = [label + " R", label + " G", label + " B"];
            _channelLabels[label] = labels;
        }

        return new Vector3(
            Slider(labels[0], value.x, min, max),
            Slider(labels[1], value.y, min, max),
            Slider(labels[2], value.z, min, max));
    }

    private string HueSampleLabel()
    {
        int count = _qualifier.HueSamples.Count;
        if (count != _hueSampleLabelCount)
        {
            _hueSampleLabelCount = count;
            _hueSampleLabel = $"Hue samples: {count}/{ColorGradeQualifier.MaxHueSamples}";
        }

        return _hueSampleLabel;
    }

    private string LutLabel()
    {
        if (!ReferenceEquals(_state.Lut, _lutLabelSource))
        {
            _lutLabelSource = _state.Lut;
            _lutLabel = _state.Lut == null
                ? NoLutLabel
                : $"{_state.Lut.Type}, size {_state.Lut.Size}, {_state.Lut.Path}";
        }

        return _lutLabel;
    }

    private static T EnumCycle<T>(string label, T value, params string[] names)
        where T : struct, Enum
    {
        using (ToolLayout.Horizontal())
        {
            GUILayout.Label(label, ToolTheme.FieldLabel, _FieldLabelWidth);
            int index = Mathf.Clamp(Convert.ToInt32(value), 0, names.Length - 1);
            if (GUILayout.Button(names[index], ToolTheme.SegmentedButton))
            {
                value = (T)(object)((index + 1) % names.Length);
            }

            return value;
        }
    }
}
