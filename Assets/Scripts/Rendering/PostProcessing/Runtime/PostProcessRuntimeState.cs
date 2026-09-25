#nullable enable

using System;
using UnityEngine;
using Kern.Core;

namespace Kern.Rendering.PostProcessing;
public static class PostProcessRuntimeState
{
    internal static Camera? MainCamera { get; private set; }
    private static uint _pipelineGeneration;

    private static float _displayPaperWhiteNits = DisplaySettings.DefaultPaperWhite;
    private static float _displayPeakBrightnessNits = DisplaySettings.DefaultPeakBrightness;
    private static ColorGradeSnapshot _colorGrade = ColorGradeSnapshot.FromLook();
    private static ColorGradeSnapshot _colorGradeSource;
    private static bool _hasColorGradeSource;
    private static PostProcessDebugView _debugView;
    private static float _compareSplit;
    private static CompareMode _compareMode;
    private static bool _compareBefore;
    private static bool _bypassPostProcessEffects;
    private static bool _temporaryBypass;

    internal static uint PipelineGeneration => _pipelineGeneration;

    internal static float DisplayPaperWhiteNits => _displayPaperWhiteNits;

    internal static float DisplayPeakBrightnessNits => _displayPeakBrightnessNits;

    internal static ColorGradeSnapshot ColorGrade => _colorGrade;

    public static bool BypassPostProcessEffects
    {
        get => _bypassPostProcessEffects;
        set
        {
            if (_bypassPostProcessEffects == value)
            {
                return;
            }

            _bypassPostProcessEffects = value;
        }
    }

    // Отладочный A/B: проходы постпроцесса не ставятся в очередь, камера без
    // постобработки URP. Меряет цену самих проходов, а не эффектов.
    public static bool SkipPasses { get; set; }

    // Калибровочный узор дисплея. Состояние держится здесь, а не в UI:
    // рисует его проход вывода, и жить оно обязано там же, где остальные
    // решения о кадре. CalibrationValue — выбранное значение в нитах.
    public static CalibrationPattern CalibrationMode { get; private set; }

    public static float CalibrationValue { get; private set; }

    public static void SetCalibrationPattern(CalibrationPattern pattern, float valueNits)
    {
        CalibrationMode = pattern;
        CalibrationValue = valueNits;
    }

    public static bool TemporaryBypass
    {
        get => _temporaryBypass;
        set
        {
            if (_temporaryBypass == value)
            {
                return;
            }

            _temporaryBypass = value;
        }
    }

    public static PostProcessDebugView DebugView
    {
        get => _debugView;
        set
        {
            PostProcessDebugView sanitized = Enum.IsDefined(typeof(PostProcessDebugView), value)
                ? value
                : PostProcessDebugView.None;
            if (_debugView == sanitized)
            {
                return;
            }

            _debugView = sanitized;
        }
    }

    public static float CompareSplit
    {
        get => _compareSplit;
        set
        {
            float sanitized = float.IsNaN(value) || float.IsInfinity(value)
                ? 0f
                : Mathf.Clamp01(value);
            if (Mathf.Approximately(_compareSplit, sanitized))
            {
                return;
            }

            _compareSplit = sanitized;
        }
    }

    public static CompareMode CompareMode
    {
        get => _compareMode;
        set
        {
            CompareMode sanitized = Enum.IsDefined(typeof(CompareMode), value)
                ? value
                : CompareMode.Off;
            if (_compareMode == sanitized)
            {
                return;
            }

            _compareMode = sanitized;
        }
    }

    public static bool CompareBefore
    {
        get => _compareBefore;
        set
        {
            if (_compareBefore == value)
            {
                return;
            }

            _compareBefore = value;
        }
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetForDomainReload()
    {
        MainCamera = null;
        _pipelineGeneration = 0;
        _displayPaperWhiteNits = DisplaySettings.DefaultPaperWhite;
        _displayPeakBrightnessNits = DisplaySettings.DefaultPeakBrightness;
        _colorGrade = ColorGradeSnapshot.FromLook();
        _colorGradeSource = default;
        _hasColorGradeSource = false;
        _debugView = PostProcessDebugView.None;
        _compareSplit = 0f;
        _compareMode = CompareMode.Off;
        _compareBefore = false;
        _bypassPostProcessEffects = false;
        _temporaryBypass = false;
        SkipPasses = false;
        CalibrationMode = CalibrationPattern.Off;
        CalibrationValue = 0f;
    }

    public static void SetDisplayCalibration(float paperWhiteNits, float peakBrightnessNits)
    {
        float sanitizedPaperWhite = FiniteClamp(
            paperWhiteNits,
            DisplaySettings.PaperWhiteMin,
            DisplaySettings.PaperWhiteMax,
            DisplaySettings.DefaultPaperWhite);
        float sanitizedPeakBrightness = Mathf.Max(
            sanitizedPaperWhite,
            FiniteClamp(
                peakBrightnessNits,
                DisplaySettings.PeakBrightnessMin,
                DisplaySettings.PeakBrightnessMax,
                DisplaySettings.DefaultPeakBrightness));
        if (Mathf.Approximately(_displayPaperWhiteNits, sanitizedPaperWhite) &&
            Mathf.Approximately(_displayPeakBrightnessNits, sanitizedPeakBrightness))
        {
            return;
        }

        _displayPaperWhiteNits = sanitizedPaperWhite;
        _displayPeakBrightnessNits = sanitizedPeakBrightness;
    }

    public static void SetColorGrade(ColorGradeSnapshot grade)
    {
        // Тот же грейд, что в прошлый раз, — ничего не делать. Сравнение по
        // содержимому: источники (рабочее место, смешивание зон) отдают
        // кривые свежими клонами, и сравнение по ссылке не совпадало ни разу —
        // Sanitized() копировал весь грейд каждый кадр, а поколение конвейера
        // сбрасывало историю постпроцесса.
        if (_hasColorGradeSource && _colorGradeSource.ContentEquals(grade))
        {
            return;
        }

        _colorGradeSource = grade;
        _hasColorGradeSource = true;
        ColorGradeSnapshot sanitized = _hasColorGradeSource
            ? grade.SanitizedReusing(_colorGrade)
            : grade.Sanitized();
        if (_colorGrade.ContentEquals(sanitized))
        {
            return;
        }

        _colorGrade = sanitized;
    }

    public static void SetMainCamera(Camera? camera)
    {
        MainCamera = camera;
    }

    private static float FiniteClamp(
        float value, float minimum, float maximum, float fallback) =>
        float.IsNaN(value) || float.IsInfinity(value)
            ? fallback
            : Mathf.Clamp(value, minimum, maximum);
}
