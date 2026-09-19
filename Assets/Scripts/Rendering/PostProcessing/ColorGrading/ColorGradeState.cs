#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Kern.Rendering.PostProcessing;

public sealed class ColorGradeState
    {
        public const int LayerCount = 6;

    public const float ExposureMin = -4f;
    public const float ExposureMax = 4f;
    public const float ExposureHardMin = -8f;
    public const float ExposureHardMax = 8f;
    public const float TemperatureMin = -100f;
    public const float TemperatureMax = 100f;
    public const float SlopeMin = 0f;
    public const float SlopeMax = 4f;
    public const float OffsetMin = -0.5f;
    public const float OffsetMax = 0.5f;
    public const float PowerMin = 0.1f;
    public const float PowerMax = 4f;
    public const float SaturationMin = 0f;
    public const float SaturationMax = 2f;
    public const float CdlSaturationMin = 0f;
    public const float CdlSaturationMax = 2f;
    public const float VibranceMin = -1f;
    public const float VibranceMax = 1f;
    public const float HueMin = -180f;
    public const float HueMax = 180f;
    public const float ContrastMin = -0.5f;
    public const float ContrastMax = 0.5f;
    public const float PivotMin = 0.1f;
    public const float PivotMax = 0.9f;
    public const float TonalAdjustmentMin = -0.5f;
    public const float TonalAdjustmentMax = 0.5f;
    public const float WhitePointMin = 0.25f;
    public const float WhitePointMax = 8f;
    public const float GreyOutMin = 0.05f;
    public const float GreyOutMax = 0.5f;
    public const float CurvePowerMin = 1f;
    public const float CurvePowerMax = 8f;
    public const float ToeStopsMin = 4f;
    public const float ToeStopsMax = 20f;
    public const float GamutCompressionStrengthMin = 0f;
    public const float GamutCompressionStrengthMax = 1f;

    private readonly bool[] _bypass = new bool[LayerCount];
    private readonly bool[] _enabled = new bool[LayerCount];
    private readonly ColorGradeHistory _history = new();

    // Кеш снимков. Окна грейдинга вызывают BeginHistoryFrame/CommitHistoryFrame
    // на каждое событие IMGUI, контроллер — ToSnapshot каждый кадр, и каждый
    // вызов клонировал все кривые дважды. Снимок пересобирается только когда
    // состояние действительно отличается от того, из которого он собран.
    // _authoredSource — сырая копия состояния (до санитизации) для сравнения.
    private ColorGradeSnapshot _authoredSource;
    private ColorGradeSnapshot _authored;
    private bool _hasAuthored;
    private int _authoredVersion;
    private ColorGradeSnapshot _preview;
    private bool _hasPreview;
    private int _previewAuthoredVersion;
    private int _previewBypassMask;
    private ColorGradeLayer? _previewSolo;

    public ColorGradeState()
    {
        ResetToLook();
    }

    public DisplayTransform Transform { get; set; }

    public float Exposure { get; set; }

    public float Contrast { get; set; }

    public float Pivot { get; set; }
    public float Shadows { get; set; }
    public float Highlights { get; set; }
    public float Blacks { get; set; }
    public float Whites { get; set; }
    public float Toe { get; set; }
    public float Shoulder { get; set; }

    public float Saturation { get; set; }

    public float CdlSaturation { get; set; }

    public float Vibrance { get; set; }

    public float Hue { get; set; }

    public float Temperature { get; set; }

    public float Tint { get; set; }

    public Vector3 Slope { get; set; }

    public Vector3 Offset { get; set; }

    public Vector3 Power { get; set; }

    public Vector3 PrimaryLift { get; set; }

    public Vector3 PrimaryGamma { get; set; }

    public Vector3 PrimaryGain { get; set; }

    public Vector3 PrimaryOffset { get; set; }

    public Vector4 PrimaryMaster { get; set; }

    public Vector3 CdlMaster { get; set; }

    public float WhitePoint { get; set; }

    public float GreyOut { get; set; }

    public float ShoulderPower { get; set; }

    public float ToePower { get; set; }

    public float ToeStops { get; set; }

    public bool GamutCompressionEnabled { get; set; } = PostProcessLook.Grade.GamutCompressionEnabled;

    public float GamutCompressionStrength { get; set; } = PostProcessLook.Grade.GamutCompressionStrength;

    public ColorGradeCurve MasterCurve { get; } = new();

    public ColorGradeCurve RedCurve { get; } = new();

    public ColorGradeCurve GreenCurve { get; } = new();

    public ColorGradeCurve BlueCurve { get; } = new();

    public ColorGradeCurve HueVsHueCurve { get; } = new(ColorGradeCurveKind.Hue);

    public ColorGradeCurve HueVsSaturationCurve { get; } = new(ColorGradeCurveKind.Hue);

    public ColorGradeCurve HueVsLuminanceCurve { get; } = new(ColorGradeCurveKind.Hue);

    public ColorGradeCurve LuminanceVsSaturationCurve { get; } = new(ColorGradeCurveKind.Range);

    public ColorGradeCurve SaturationVsSaturationCurve { get; } = new(ColorGradeCurveKind.Range);

    public ColorGradeQualifier Qualifier { get; } = new();

    public ColorGradeCubeLut? Lut { get; private set; }

    public string LutPath { get; private set; } = string.Empty;

    public float LutIntensity { get; set; }

    public ColorGradeLutColorSpace LutColorSpace { get; set; }

    public ColorGradeLayer? Solo { get; set; }

    public bool CanUndo => _history.CanUndo;

    public bool CanRedo => _history.CanRedo;

    public void BeginHistoryFrame() => _history.BeginHistoryFrame(this);

    public void CommitHistoryFrame() => _history.CommitHistoryFrame(this);

    public void CancelHistoryFrame() => _history.CancelHistoryFrame();

    public bool Undo() => _history.Undo(this);

    public bool Redo() => _history.Redo(this);

    public bool IsBypassed(ColorGradeLayer layer) => _bypass[(int)layer];

    public bool IsEnabled(ColorGradeLayer layer) => _enabled[(int)layer];

    public void SetEnabled(ColorGradeLayer layer, bool enabled)
    {
        _enabled[(int)layer] = enabled;
        if (!enabled && Solo == layer)
        {
            Solo = null;
        }
    }

    public int EnabledMask
    {
        get
        {
            int mask = 0;
            for (int index = 0; index < LayerCount; index++)
            {
                if (_enabled[index])
                {
                    mask |= 1 << index;
                }
            }

            return mask;
        }
        set
        {
            int validMask = value & ((1 << LayerCount) - 1);
            for (int index = 0; index < LayerCount; index++)
            {
                _enabled[index] = (validMask & (1 << index)) != 0;
            }
        }
    }

    public void SetBypassed(ColorGradeLayer layer, bool bypassed) =>
        _bypass[(int)layer] = bypassed;

    public int BypassedCount
    {
        get
        {
            int count = 0;
            for (int index = 0; index < LayerCount; index++)
            {
                if (_bypass[index])
                {
                    count++;
                }
            }

            return count;
        }
    }

    public void ToggleSolo(ColorGradeLayer layer)
    {
        Solo = Solo == layer ? null : layer;
    }

    public int BypassMask
    {
        get
        {
            int mask = 0;
            for (int index = 0; index < LayerCount; index++)
            {
                if (_bypass[index])
                {
                    mask |= 1 << index;
                }
            }

            return mask;
        }
        set
        {
            int validMask = value & ((1 << LayerCount) - 1);
            for (int index = 0; index < LayerCount; index++)
            {
                _bypass[index] = (validMask & (1 << index)) != 0;
            }
        }
    }

    public bool HasPreviewOverrides => Solo.HasValue || BypassMask != 0;

    public void ClearPreviewOverrides()
    {
        Solo = null;
        Array.Clear(_bypass, 0, _bypass.Length);
    }

    public bool IsActive(ColorGradeLayer layer)
    {
        if (!IsEnabled(layer))
        {
            return false;
        }

        if (Solo.HasValue)
        {
            return Solo.Value == layer;
        }

        return !_bypass[(int)layer];
    }

    public void ResetToLook() => ColorGradeDefaults.ResetToLook(this);

    public void ResetLayer(ColorGradeLayer layer) => ColorGradeDefaults.ResetLayer(this, layer);

    public void Sanitize() => ColorGradeSanitizer.Sanitize(this);

    public ColorGradeSnapshot ToAuthoredSnapshot()
    {
        if (_hasAuthored && SourceMatches(in _authoredSource))
        {
            return _authored;
        }

        _authoredSource = BuildSourceSnapshot();
        _authored = _authoredSource.Sanitized();
        _hasAuthored = true;
        _authoredVersion++;
        return _authored;
    }

    private bool SourceMatches(in ColorGradeSnapshot source) =>
        source.EnabledMask == EnabledMask &&
        source.Transform == Transform &&
        source.Exposure == Exposure &&
        source.CdlSaturation == CdlSaturation &&
        source.WhitePoint == WhitePoint &&
        source.Temperature == Temperature &&
        source.Tint == Tint &&
        source.Slope == Slope &&
        source.Offset == Offset &&
        source.Power == Power &&
        source.PrimaryLift == PrimaryLift &&
        source.PrimaryGamma == PrimaryGamma &&
        source.PrimaryGain == PrimaryGain &&
        source.PrimaryOffset == PrimaryOffset &&
        source.PrimaryMaster == PrimaryMaster &&
        source.Vibrance == Vibrance &&
        source.Hue == Hue &&
        source.CdlMaster == CdlMaster &&
        source.Pivot == Pivot &&
        source.Shadows == Shadows &&
        source.Highlights == Highlights &&
        source.Blacks == Blacks &&
        source.Whites == Whites &&
        source.Toe == Toe &&
        source.Shoulder == Shoulder &&
        source.GreyOut == GreyOut &&
        source.ShoulderPower == ShoulderPower &&
        source.ToePower == ToePower &&
        source.ToeStops == ToeStops &&
        source.GamutCompressionEnabled == GamutCompressionEnabled &&
        source.GamutCompressionStrength == GamutCompressionStrength &&
        ReferenceEquals(source.Lut, Lut) &&
        source.LutIntensity == LutIntensity &&
        source.LutColorSpace == LutColorSpace &&
        source.MasterCurve.ContentEquals(MasterCurve) &&
        source.RedCurve.ContentEquals(RedCurve) &&
        source.GreenCurve.ContentEquals(GreenCurve) &&
        source.BlueCurve.ContentEquals(BlueCurve) &&
        source.HueVsHueCurve.ContentEquals(HueVsHueCurve) &&
        source.HueVsSaturationCurve.ContentEquals(HueVsSaturationCurve) &&
        source.HueVsLuminanceCurve.ContentEquals(HueVsLuminanceCurve) &&
        source.LuminanceVsSaturationCurve.ContentEquals(LuminanceVsSaturationCurve) &&
        source.SaturationVsSaturationCurve.ContentEquals(SaturationVsSaturationCurve) &&
        source.Qualifier.ContentEquals(Qualifier);

    private ColorGradeSnapshot BuildSourceSnapshot() => new ColorGradeSnapshot
    {
        EnabledMask = EnabledMask,
        Transform = Transform,
        Exposure = Exposure,
        CdlSaturation = CdlSaturation,
        WhitePoint = WhitePoint,
        Temperature = Temperature,
        Tint = Tint,
        Slope = Slope,
        Offset = Offset,
        Power = Power,
        PrimaryLift = PrimaryLift,
        PrimaryGamma = PrimaryGamma,
        PrimaryGain = PrimaryGain,
        PrimaryOffset = PrimaryOffset,
        PrimaryMaster = PrimaryMaster,
        Vibrance = Vibrance,
        Hue = Hue,
        CdlMaster = CdlMaster,
        Pivot = Pivot,
        Shadows = Shadows,
        Highlights = Highlights,
        Blacks = Blacks,
        Whites = Whites,
        Toe = Toe,
        Shoulder = Shoulder,
        GreyOut = GreyOut,
        ShoulderPower = ShoulderPower,
        ToePower = ToePower,
        ToeStops = ToeStops,
        GamutCompressionEnabled = GamutCompressionEnabled,
        GamutCompressionStrength = GamutCompressionStrength,
        MasterCurve = MasterCurve.Clone(),
        RedCurve = RedCurve.Clone(),
        GreenCurve = GreenCurve.Clone(),
        BlueCurve = BlueCurve.Clone(),
        HueVsHueCurve = HueVsHueCurve.Clone(),
        HueVsSaturationCurve = HueVsSaturationCurve.Clone(),
        HueVsLuminanceCurve = HueVsLuminanceCurve.Clone(),
        LuminanceVsSaturationCurve = LuminanceVsSaturationCurve.Clone(),
        SaturationVsSaturationCurve = SaturationVsSaturationCurve.Clone(),
        Qualifier = Qualifier.Clone(),
        Lut = Lut,
        LutIntensity = LutIntensity,
        LutColorSpace = LutColorSpace,
    };

    public ColorGradeSnapshot ToSnapshot()
    {
        ColorGradeSnapshot authored = ToAuthoredSnapshot();
        int bypassMask = BypassMask;
        if (_hasPreview &&
            _previewAuthoredVersion == _authoredVersion &&
            _previewBypassMask == bypassMask &&
            _previewSolo == Solo)
        {
            return _preview;
        }

        _preview = ColorGradePreviewBuilder.BuildPreviewSnapshot(this, in authored);
        _hasPreview = true;
        _previewAuthoredVersion = _authoredVersion;
        _previewBypassMask = bypassMask;
        _previewSolo = Solo;
        return _preview;
    }

    public float EffectiveExposure => IsActive(ColorGradeLayer.Exposure) ? Exposure : 0f;

    public float EffectiveContrast => IsActive(ColorGradeLayer.Contrast) ? Contrast : 0f;

    public float EffectiveSaturation => IsActive(ColorGradeLayer.Saturation) ? Saturation : 1f;

    private static float FiniteClamp(float value, float minimum, float maximum, float fallback) =>
        float.IsNaN(value) || float.IsInfinity(value)
            ? fallback
            : Mathf.Clamp(value, minimum, maximum);

    private static Vector3 FiniteClamp(Vector3 value, float minimum, float maximum, Vector3 fallback) =>
        new(
            FiniteClamp(value.x, minimum, maximum, fallback.x),
            FiniteClamp(value.y, minimum, maximum, fallback.y),
            FiniteClamp(value.z, minimum, maximum, fallback.z));

    public bool LoadLut(string path, out string error)
    {
        if (!ColorGradeCubeLut.TryLoad(path, out ColorGradeCubeLut? lut, out error) || lut == null)
        {
            return false;
        }

        Lut?.Dispose();
        Lut = lut;
        LutPath = path;
        LutIntensity = 1f;
        return true;
    }

    public void ClearLut()
    {
        Lut?.Dispose();
        Lut = null;
        LutPath = string.Empty;
        LutIntensity = 0f;
    }
}
