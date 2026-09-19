#nullable enable

using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Kern.Rendering.PostProcessing;

public sealed class ColorGradeHistory
{
    private readonly Stack<ColorGradeSnapshot> _undo = [];
    private readonly Stack<ColorGradeSnapshot> _redo = [];
    private ColorGradeSnapshot? _historyFrame;

    public bool CanUndo => _undo.Count > 0;

    public bool CanRedo => _redo.Count > 0;

    public void BeginHistoryFrame(ColorGradeState state)
    {
        _historyFrame ??= state.ToAuthoredSnapshot();
    }

    public void CommitHistoryFrame(ColorGradeState state)
    {
        if (!_historyFrame.HasValue)
        {
            return;
        }

        ColorGradeSnapshot before = _historyFrame.Value;
        _historyFrame = null;
        ColorGradeSnapshot after = state.ToAuthoredSnapshot();
        if (SnapshotsEqual(before, after))
        {
            return;
        }

        _undo.Push(before);
        _redo.Clear();
        TrimHistory(_undo);
    }

    public void CancelHistoryFrame() => _historyFrame = null;

    public bool Undo(ColorGradeState state)
    {
        if (_undo.Count == 0)
        {
            return false;
        }

        ColorGradeSnapshot current = state.ToAuthoredSnapshot();
        ColorGradeSnapshot previous = _undo.Pop();
        _redo.Push(current);
        RestoreSnapshot(state, previous);
        return true;
    }

    public bool Redo(ColorGradeState state)
    {
        if (_redo.Count == 0)
        {
            return false;
        }

        ColorGradeSnapshot current = state.ToAuthoredSnapshot();
        ColorGradeSnapshot next = _redo.Pop();
        _undo.Push(current);
        RestoreSnapshot(state, next);
        return true;
    }

    public void Clear()
    {
        _undo.Clear();
        _redo.Clear();
        _historyFrame = null;
    }

    private static void TrimHistory(Stack<ColorGradeSnapshot> history)
    {
        while (history.Count > 64)
        {
            ColorGradeSnapshot[] entries = history.ToArray();
            history.Clear();
            for (int index = entries.Length - 2; index >= 0; index--)
            {
                history.Push(entries[index]);
            }
        }
    }

    public static void RestoreSnapshot(ColorGradeState state, ColorGradeSnapshot snapshot)
    {
        snapshot = snapshot.Sanitized();
        state.EnabledMask = snapshot.EnabledMask;
        state.Transform = snapshot.Transform;
        state.Exposure = snapshot.Exposure;
        state.Contrast = snapshot.Contrast;
        state.Pivot = snapshot.Pivot;
        state.Shadows = snapshot.Shadows;
        state.Highlights = snapshot.Highlights;
        state.Blacks = snapshot.Blacks;
        state.Whites = snapshot.Whites;
        state.Toe = snapshot.Toe;
        state.Shoulder = snapshot.Shoulder;
        state.Saturation = snapshot.Saturation;
        state.CdlSaturation = snapshot.CdlSaturation;
        state.Vibrance = snapshot.Vibrance;
        state.Hue = snapshot.Hue;
        state.Temperature = snapshot.Temperature;
        state.Tint = snapshot.Tint;
        state.Slope = snapshot.Slope;
        state.Offset = snapshot.Offset;
        state.Power = snapshot.Power;
        state.PrimaryLift = snapshot.PrimaryLift;
        state.PrimaryGamma = snapshot.PrimaryGamma;
        state.PrimaryGain = snapshot.PrimaryGain;
        state.PrimaryOffset = snapshot.PrimaryOffset;
        state.PrimaryMaster = snapshot.PrimaryMaster;
        state.CdlMaster = snapshot.CdlMaster;
        state.WhitePoint = snapshot.WhitePoint;
        state.GreyOut = snapshot.GreyOut;
        state.ShoulderPower = snapshot.ShoulderPower;
        state.ToePower = snapshot.ToePower;
        state.ToeStops = snapshot.ToeStops;
        state.GamutCompressionEnabled = snapshot.GamutCompressionEnabled;
        state.GamutCompressionStrength = snapshot.GamutCompressionStrength;
        CopyCurve(state.MasterCurve, snapshot.MasterCurve);
        CopyCurve(state.RedCurve, snapshot.RedCurve);
        CopyCurve(state.GreenCurve, snapshot.GreenCurve);
        CopyCurve(state.BlueCurve, snapshot.BlueCurve);
        CopyCurve(state.HueVsHueCurve, snapshot.HueVsHueCurve);
        CopyCurve(state.HueVsSaturationCurve, snapshot.HueVsSaturationCurve);
        CopyCurve(state.HueVsLuminanceCurve, snapshot.HueVsLuminanceCurve);
        CopyCurve(state.LuminanceVsSaturationCurve, snapshot.LuminanceVsSaturationCurve);
        CopyCurve(state.SaturationVsSaturationCurve, snapshot.SaturationVsSaturationCurve);
        CopyQualifier(state.Qualifier, snapshot.Qualifier);
        if (snapshot.Lut == null)
        {
            state.ClearLut();
        }
        else
        {
            if (!ReferenceEquals(state.Lut, snapshot.Lut))
            {
                state.ClearLut();
                state.LoadLut(snapshot.Lut.Path, out _);
            }

            state.LutIntensity = snapshot.LutIntensity;
            state.LutColorSpace = snapshot.LutColorSpace;
        }

        state.Sanitize();
    }

    private static void CopyCurve(ColorGradeCurve target, ColorGradeCurve source)
    {
        target.Load(
            source.Points.Take(source.PointCount).ToArray(),
            (int)source.Interpolation);
    }

    private static void CopyQualifier(ColorGradeQualifier target, ColorGradeQualifier source)
    {
        target.Reset();
        target.Enabled = source.Enabled;
        target.Invert = source.Invert;
        target.HueCenter = source.HueCenter;
        target.HueWidth = source.HueWidth;
        target.HueSoftness = source.HueSoftness;
        target.SaturationCenter = source.SaturationCenter;
        target.SaturationWidth = source.SaturationWidth;
        target.SaturationSoftness = source.SaturationSoftness;
        target.LuminanceCenter = source.LuminanceCenter;
        target.LuminanceWidth = source.LuminanceWidth;
        target.LuminanceSoftness = source.LuminanceSoftness;
        target.HueShift = source.HueShift;
        target.Saturation = source.Saturation;
        target.Exposure = source.Exposure;
        target.Temperature = source.Temperature;
        target.Tint = source.Tint;
        target.Lift = source.Lift;
        target.Gamma = source.Gamma;
        target.Gain = source.Gain;
        foreach (float sample in source.HueSamples)
        {
            target.AddHueSample(sample);
        }
    }

    private static bool SnapshotsEqual(ColorGradeSnapshot left, ColorGradeSnapshot right)
    {
        return left.EnabledMask == right.EnabledMask &&
            left.Transform == right.Transform &&
            Approximately(left.Exposure, right.Exposure) &&
            Approximately(left.Contrast, right.Contrast) &&
            left.Pivot == right.Pivot &&
            left.Shadows == right.Shadows &&
            left.Highlights == right.Highlights &&
            left.Blacks == right.Blacks &&
            left.Whites == right.Whites &&
            left.Toe == right.Toe &&
            left.Shoulder == right.Shoulder &&
            left.Saturation == right.Saturation &&
            left.CdlSaturation == right.CdlSaturation &&
            left.Vibrance == right.Vibrance &&
            left.Hue == right.Hue &&
            left.Temperature == right.Temperature &&
            left.Tint == right.Tint &&
            left.Slope == right.Slope &&
            left.Offset == right.Offset &&
            left.Power == right.Power &&
            left.PrimaryLift == right.PrimaryLift &&
            left.PrimaryGamma == right.PrimaryGamma &&
            left.PrimaryGain == right.PrimaryGain &&
            left.PrimaryOffset == right.PrimaryOffset &&
            left.PrimaryMaster == right.PrimaryMaster &&
            left.CdlMaster == right.CdlMaster &&
            left.WhitePoint == right.WhitePoint &&
            left.GreyOut == right.GreyOut &&
            left.ShoulderPower == right.ShoulderPower &&
            left.ToePower == right.ToePower &&
            left.ToeStops == right.ToeStops &&
            left.GamutCompressionEnabled == right.GamutCompressionEnabled &&
            left.GamutCompressionStrength == right.GamutCompressionStrength &&
            ReferenceEquals(left.Lut, right.Lut) &&
            left.LutIntensity == right.LutIntensity &&
            left.LutColorSpace == right.LutColorSpace &&
            CurvesEqual(left.MasterCurve, right.MasterCurve) &&
            CurvesEqual(left.RedCurve, right.RedCurve) &&
            CurvesEqual(left.GreenCurve, right.GreenCurve) &&
            CurvesEqual(left.BlueCurve, right.BlueCurve) &&
            CurvesEqual(left.HueVsHueCurve, right.HueVsHueCurve) &&
            CurvesEqual(left.HueVsSaturationCurve, right.HueVsSaturationCurve) &&
            CurvesEqual(left.HueVsLuminanceCurve, right.HueVsLuminanceCurve) &&
            CurvesEqual(left.LuminanceVsSaturationCurve, right.LuminanceVsSaturationCurve) &&
            CurvesEqual(left.SaturationVsSaturationCurve, right.SaturationVsSaturationCurve) &&
            QualifiersEqual(left.Qualifier, right.Qualifier);
    }

    private static bool CurvesEqual(ColorGradeCurve left, ColorGradeCurve right) =>
        left.PointCount == right.PointCount &&
        left.Interpolation == right.Interpolation &&
        left.Points.SequenceEqual(right.Points);

    private static bool QualifiersEqual(ColorGradeQualifier left, ColorGradeQualifier right) =>
        left.Enabled == right.Enabled &&
        left.Invert == right.Invert &&
        left.HueCenter == right.HueCenter &&
        left.HueWidth == right.HueWidth &&
        left.HueSoftness == right.HueSoftness &&
        left.SaturationCenter == right.SaturationCenter &&
        left.SaturationWidth == right.SaturationWidth &&
        left.SaturationSoftness == right.SaturationSoftness &&
        left.LuminanceCenter == right.LuminanceCenter &&
        left.LuminanceWidth == right.LuminanceWidth &&
        left.LuminanceSoftness == right.LuminanceSoftness &&
        left.HueShift == right.HueShift &&
        left.Saturation == right.Saturation &&
        left.Exposure == right.Exposure &&
        left.Temperature == right.Temperature &&
        left.Tint == right.Tint &&
        left.Lift == right.Lift &&
        left.Gamma == right.Gamma &&
        left.Gain == right.Gain &&
        left.HueSamples.SequenceEqual(right.HueSamples);

    private static bool Approximately(float left, float right) =>
        Mathf.Abs(left - right) <= 1e-5f;
}
