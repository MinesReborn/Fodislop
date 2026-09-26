#nullable enable

using System;
using System.Collections.Generic;
using System.Globalization;
using Kern.Tools.Imgui;
using UnityEngine;

namespace Kern.Rendering.PostProcessing.Workbench;

internal sealed class GradingLayerControlsDrawer
{
    private readonly ColorGradeState _state;
    private readonly ColorGradeZones _zones;
    private readonly Dictionary<string, string> _numberText = [];

    // Имена контролов и id каналов выводятся из id слайдера. Склейка на
    // каждое событие IMGUI давала десятки строк за кадр на одно окно.
    private readonly Dictionary<string, string> _controlNames = [];
    private readonly Dictionary<string, string[]> _channelIds = [];

    private static readonly GUILayoutOption _SliderLabelWidth = GUILayout.Width(122f);
    private static readonly GUILayoutOption _SliderFieldWidth = GUILayout.Width(64f);

    // GUI.GetNameOfFocusedControl() каждый раз возвращает новую строку из
    // нативного кода, а вызывался он в каждом слайдере в каждом событии.
    // Имя меняется только вместе с фокусом клавиатуры.
    private int _focusedNameKeyboardControl = int.MinValue;
    private string _focusedName = string.Empty;

    private string FocusedControlName()
    {
        int keyboardControl = GUIUtility.keyboardControl;
        if (keyboardControl != _focusedNameKeyboardControl)
        {
            _focusedNameKeyboardControl = keyboardControl;
            _focusedName = GUI.GetNameOfFocusedControl();
        }

        return _focusedName;
    }
    private readonly GradingCurveEditorDrawer _curveDrawer = new();
    private readonly GradingActionsDrawer _actionsDrawer;
    private ColorGradeLayer? _bypassLayerRequested;
    private bool _bypassValueRequested;
    private bool _soloChangeRequested;
    private ColorGradeLayer? _soloRequested;
    private bool _clearBypassesRequested;
    private string? _invalidNumberId;
    public GradingLayerControlsDrawer(ColorGradeState state, ColorGradeZones zones)
    {
        _state = state;
        _zones = zones;
        _actionsDrawer = new GradingActionsDrawer(state, zones);
    }

    public string? Status => _actionsDrawer.Status;

    public bool StatusIsError => _actionsDrawer.StatusIsError;

    public void SetStatus(bool success, string successMessage, string failureMessage) =>
        _actionsDrawer.SetStatus(success, successMessage, failureMessage);

    public void ClearStatus() => _actionsDrawer.ClearStatus();

    public void RemoveNumberText(string key) => _numberText.Remove(key);

    public void ResetState()
    {
        GradingPrimaryWheelDrawer.ReleaseWheelTexture();
        _numberText.Clear();
        _curveDrawer.ResetState();
        _actionsDrawer.ResetState();
        _bypassLayerRequested = null;
        _bypassValueRequested = false;
        _soloChangeRequested = false;
        _soloRequested = null;
        _clearBypassesRequested = false;
    }

    public void ClearNumberCache()
    {
        _numberText.Clear();
    }

    public void RequestBypass(ColorGradeLayer layer, bool bypass)
    {
        _bypassLayerRequested = layer;
        _bypassValueRequested = bypass;
    }

    public void RequestSolo(ColorGradeLayer? layer)
    {
        _soloChangeRequested = true;
        _soloRequested = layer;
    }

    public void RequestClearBypasses()
    {
        _clearBypassesRequested = true;
    }

    public void DrawLayerControls(ColorGradeLayer layer)
    {
        bool active = _state.IsActive(layer);
        bool previousGuiEnabled = GUI.enabled;
        if (!active)
        {
            GUI.enabled = false;
        }

        switch (layer)
        {
            case ColorGradeLayer.Exposure:
                GradingLayerSpecificControlsDrawer.DrawExposureControls(_state, this);
                break;

            case ColorGradeLayer.WhiteBalance:
                GradingLayerSpecificControlsDrawer.DrawWhiteBalanceControls(_state, this);
                break;

            case ColorGradeLayer.Cdl:
                GradingLayerSpecificControlsDrawer.DrawCdlControls(_state, this);
                break;

            case ColorGradeLayer.Saturation:
                GradingLayerSpecificControlsDrawer.DrawSaturationControls(_state, this, _curveDrawer);
                break;

            case ColorGradeLayer.Contrast:
                GradingLayerSpecificControlsDrawer.DrawContrastControls(_state, this);
                break;

            case ColorGradeLayer.Curve:
                GradingLayerSpecificControlsDrawer.DrawCurveControls(_state, this, _curveDrawer);
                break;

            default:
                break;
        }

        GUI.enabled = previousGuiEnabled;
        if (!active)
        {
            string reason = _state.Solo.HasValue
                ? $"Слой выключен (активно соло другого слоя: {GetLayerTitle(_state.Solo.Value)})"
                : "Слой в обходе — значения не влияют на кадр";
            GUILayout.Label(reason, ToolTheme.WarningLabel);
        }
    }

    public string GetLayerName(ColorGradeLayer layer) => GetLayerTitle(layer);

    public static string GetLayerTitle(ColorGradeLayer layer) => layer switch
    {
        ColorGradeLayer.Exposure => "Экспозиция",
        ColorGradeLayer.WhiteBalance => "Баланс белого",
        ColorGradeLayer.Cdl => "ASC CDL",
        ColorGradeLayer.Saturation => "Насыщенность",
        ColorGradeLayer.Contrast => "Контраст",
        ColorGradeLayer.Curve => "Кривая",
        _ => layer.ToString(),
    };

    public void DrawActions(GUIStyle sectionStyle, GUIStyle wrappedLabelStyle) =>
        _actionsDrawer.DrawActions(sectionStyle, wrappedLabelStyle);

    public void ApplyPendingActions()
    {
        if (Event.current.type != EventType.Layout)
        {
            return;
        }

        if (_bypassLayerRequested.HasValue)
        {
            _state.SetBypassed(_bypassLayerRequested.Value, _bypassValueRequested);
            _bypassLayerRequested = null;
        }

        if (_soloChangeRequested)
        {
            _state.Solo = _soloRequested;
            _soloChangeRequested = false;
            _soloRequested = null;
        }

        if (_clearBypassesRequested)
        {
            for (int i = 0; i < 6; i++)
            {
                _state.SetBypassed((ColorGradeLayer)i, false);
            }

            _clearBypassesRequested = false;
        }

        _actionsDrawer.ApplyPendingActions(this);
    }

    public float Slider(string id, string label, float value, float minimum, float maximum)
    {
        if (!_numberText.TryGetValue(id, out string? text))
        {
            text = value.ToString("0.###", CultureInfo.InvariantCulture);
            _numberText[id] = text;
        }

        using (ToolLayout.Horizontal())
        {
            GUILayout.Label(label, ToolTheme.FieldLabel, _SliderLabelWidth);
            float sliderMinimum = minimum;
            float sliderMaximum = maximum;
            if (Event.current.shift)
            {
                float fineRange = (maximum - minimum) * 0.1f;
                sliderMinimum = Mathf.Max(minimum, value - fineRange);
                sliderMaximum = Mathf.Min(maximum, value + fineRange);
            }

            float result = GUILayout.HorizontalSlider(value, sliderMinimum, sliderMaximum);
            if (!Mathf.Approximately(result, value))
            {
                text = result.ToString("0.###", CultureInfo.InvariantCulture);
                _numberText[id] = text;
            }

            if (!_controlNames.TryGetValue(id, out string? controlName))
            {
                controlName = "grade." + id;
                _controlNames[id] = controlName;
            }

            GUI.SetNextControlName(controlName);
            string edited = GUILayout.TextField(text, _SliderFieldWidth);
            if (edited != text)
            {
                edited = edited.Replace(',', '.');
                _numberText[id] = edited;
                if (_invalidNumberId == id)
                {
                    _invalidNumberId = null;
                    _actionsDrawer.ClearStatus();
                }

                if (float.TryParse(
                        edited,
                        NumberStyles.Float,
                        CultureInfo.InvariantCulture,
                        out float parsed) &&
                    !float.IsNaN(parsed) &&
                    !float.IsInfinity(parsed))
                {
                    result = Mathf.Clamp(parsed, minimum, maximum);
                }
            }

            bool focused = FocusedControlName() == controlName;
            if (focused &&
                Event.current.type == EventType.KeyDown &&
                (Event.current.keyCode == KeyCode.Return ||
                 Event.current.keyCode == KeyCode.KeypadEnter))
            {
                GUI.FocusControl(null);
                focused = false;
                Event.current.Use();
            }

            if (!focused)
            {
                if (float.TryParse(
                        _numberText[id],
                        NumberStyles.Float,
                        CultureInfo.InvariantCulture,
                        out float committed) &&
                    !float.IsNaN(committed) &&
                    !float.IsInfinity(committed))
                {
                    // Без фокуса источник истины — значение, а не текст. Раньше
                    // здесь было `result = committed`: слайдер возвращал число из
                    // своей строки и на следующем же событии откатывал всё, что
                    // поменяли в обход него, — колесо коррекции, перетаскивание
                    // точки кривой, пипетку. Набранный текст уже применён в той
                    // ветке, где поле было в фокусе, так что терять нечего.
                    // Порог — точность формата "0.###": без него значение с
                    // четвёртым знаком переформатировалось бы в каждом событии.
                    if (Mathf.Abs(committed - result) > 0.0005f)
                    {
                        _numberText[id] = result.ToString("0.###", CultureInfo.InvariantCulture);
                    }
                }
else
                    {
                        _numberText[id] = value.ToString("0.###", CultureInfo.InvariantCulture);
                        _invalidNumberId = id;
                        _actionsDrawer.SetStatus(false, string.Empty, $"Некорректное число «{label}»; оставлено предыдущее значение.");
                    }
            }

            if (Event.current.type == EventType.MouseDown &&
                Event.current.button == 0 &&
                Event.current.clickCount == 2 &&
                GUILayoutUtility.GetLastRect().Contains(Event.current.mousePosition))
            {
                result = NeutralValue(id, minimum, maximum);
                _numberText[id] = result.ToString("0.###", CultureInfo.InvariantCulture);
                Event.current.Use();
            }

            return result;
        }
    }

    private static float NeutralValue(string id, float minimum, float maximum)
    {
        float neutral = id switch
        {
            "exposure" or "black-point" or "highlight-recovery" => 0f,
            "input-white-point" or "whitepoint" or "white-point" => 1f,
            "grey-out" => 0.18f,
            "curve-slope" => 1f,
            "gamut-compression" => 1f,
            "toe-stops" => 12f,
            "shoulder-power" => 4f,
            "toe-power" => 1.6f,
            "path-power" => 3f,
            "path-to-white" => 0f,
            "saturation" or "cdl.saturation" => 1f,
            "pivot" => 0.5f,
            "hue" or "temperature" or "tint" => 0f,
            _ when id.EndsWith(".slope.r", StringComparison.Ordinal) ||
                id.EndsWith(".slope.g", StringComparison.Ordinal) ||
                id.EndsWith(".slope.b", StringComparison.Ordinal) ||
                id == "master.slope" => 1f,
            _ when id.EndsWith(".power.r", StringComparison.Ordinal) ||
                id.EndsWith(".power.g", StringComparison.Ordinal) ||
                id.EndsWith(".power.b", StringComparison.Ordinal) ||
                id == "master.power" ||
                id.StartsWith("primary.gamma.", StringComparison.Ordinal) ||
                id == "primary.master.gamma" => 1f,
            _ when id.StartsWith("primary.gain.", StringComparison.Ordinal) ||
                id == "primary.master.gain" => 1f,
            _ when id.Contains("center", StringComparison.OrdinalIgnoreCase) =>
                id.Contains("hue", StringComparison.OrdinalIgnoreCase) ? 120f : 0.5f,
            _ when id.Contains("multiplier", StringComparison.OrdinalIgnoreCase) => 1f,
            _ when id.Contains("shift", StringComparison.OrdinalIgnoreCase) => 0f,
            _ when id.Contains("power", StringComparison.OrdinalIgnoreCase) => 1f,
            _ => 0f,
        };
        return Mathf.Clamp(neutral, minimum, maximum);
    }

    public Vector3 TripletSlider(
        string id,
        string label,
        Vector3 value,
        float minimum,
        float maximum)
    {
        GUILayout.Label(label, ToolTheme.SectionLabel);
        string[] ids = ChannelIds(id);
        return new Vector3(
            Slider(ids[0], "  R", value.x, minimum, maximum),
            Slider(ids[1], "  G", value.y, minimum, maximum),
            Slider(ids[2], "  B", value.z, minimum, maximum));
    }

    private string[] ChannelIds(string id)
    {
        if (!_channelIds.TryGetValue(id, out string[]? ids))
        {
            ids = [id + ".r", id + ".g", id + ".b"];
            _channelIds[id] = ids;
        }

        return ids;
    }

}
