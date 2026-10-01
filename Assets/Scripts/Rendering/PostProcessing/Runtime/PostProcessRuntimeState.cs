#nullable enable

using System;
using UnityEngine;
using UnityEngine.Rendering;
using Kern.Core;

namespace Kern.Rendering.PostProcessing;
public static class PostProcessRuntimeState
{
    internal static Camera? MainCamera { get; private set; }

    // Explicit production-image observation. The test records a GPU readback in
    // the real pass before its input is discarded. All arguments are borrowed
    // for this call; the fixture must clear the callback during teardown.
    internal static Action<CommandBuffer, RenderTexture, Vector4>? DiagnosticWorldImage { get; set; }

    // Observation only: production scene/prefilter/additive targets. The
    // callback records readbacks into this frame's command buffer and cannot
    // retain borrowed textures. Null incurs no diagnostic render passes.
    internal static Action<CommandBuffer, string, RenderTexture, Vector4>? DiagnosticBloomImage { get; set; }

    // Borrowed camera for an explicitly requested offscreen production benchmark.
    // Fixture owns registration and teardown; ordinary offscreen cameras stay excluded.
    public static Camera? DiagnosticOffscreenCamera { get; set; }
    // Legacy fusion A/B is available only when DiagnosticLegacyBloom is explicit.
    // Ordinary production uses the small world-grid bloom pyramid.
    public static bool DiagnosticUnfusedBloom { get; set; }
    public static bool DiagnosticLegacyBloom { get; set; }
    // Explicit before/after reference for the registered benchmark camera.
    // Production cameras always keep the fixed world grid.
    internal static bool DiagnosticFullResolutionWorld { get; set; }
    public static int DiagnosticBloomDispatches { get; private set; }
    public static int DiagnosticBloomFrame { get; private set; } = -1;

    internal static void RecordBloomDispatches(int dispatches, EntityId cameraId)
    {
        if (DiagnosticOffscreenCamera != null && DiagnosticOffscreenCamera.GetEntityId() == cameraId)
        {
            DiagnosticBloomDispatches = dispatches;
            DiagnosticBloomFrame = Time.frameCount;
        }
    }
    public static int DiagnosticSceneFrame { get; private set; } = -1;
    public static int DiagnosticDisplayFrame { get; private set; } = -1;

    internal static void RecordDiagnosticPass(Camera camera, bool displayPass)
    {
        if (camera != DiagnosticOffscreenCamera)
        {
            return;
        }

        if (displayPass)
        {
            DiagnosticDisplayFrame = Time.frameCount;
        }
        else
        {
            DiagnosticSceneFrame = Time.frameCount;
        }
    }

    private static float _displayPaperWhiteNits = DisplaySettings.DefaultPaperWhite;
    private static float _displayPeakBrightnessNits = DisplaySettings.DefaultPeakBrightness;
    private static PostProcessDebugView _debugView;
    private static float _compareSplit;
    private static CompareMode _compareMode;
    private static bool _compareBefore;
    private static bool _bypassPostProcessEffects;
    private static bool _temporaryBypass;

    internal static float DisplayPaperWhiteNits => _displayPaperWhiteNits;

    internal static float DisplayPeakBrightnessNits => _displayPeakBrightnessNits;

    // Цветокоррекция кадра — один LUT. Присылает его сервер как эффект;
    // владеет объектом тот, кто его передал, и он же его освобождает.
    internal static ColorGradeCubeLut? Lut { get; private set; }

    internal static float LutIntensity { get; private set; }

    internal static ColorGradeLutColorSpace LutColorSpace { get; private set; }

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
        DiagnosticOffscreenCamera = null;
        DiagnosticWorldImage = null;
        DiagnosticBloomImage = null;
        DiagnosticLegacyBloom = false;
        DiagnosticFullResolutionWorld = false;
        DiagnosticUnfusedBloom = false;
        DiagnosticBloomDispatches = 0;
        DiagnosticBloomFrame = -1;
        DiagnosticSceneFrame = -1;
        DiagnosticDisplayFrame = -1;
        _displayPaperWhiteNits = DisplaySettings.DefaultPaperWhite;
        _displayPeakBrightnessNits = DisplaySettings.DefaultPeakBrightness;
        Lut = null;
        LutIntensity = 0f;
        LutColorSpace = ColorGradeLutColorSpace.LinearRec709;
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

    // null или нулевая сила выключают LUT; проход вывода тогда не тратит на
    // него ни чтения.
    public static void SetLut(
        ColorGradeCubeLut? lut,
        float intensity,
        ColorGradeLutColorSpace colorSpace = ColorGradeLutColorSpace.LinearRec709)
    {
        float sanitized = FiniteClamp(intensity, 0f, 1f, 0f);
        Lut = sanitized > 0f ? lut : null;
        LutIntensity = Lut == null ? 0f : sanitized;
        LutColorSpace = colorSpace is ColorGradeLutColorSpace.LinearRec709 or ColorGradeLutColorSpace.SrgbRec709
            ? colorSpace
            : ColorGradeLutColorSpace.LinearRec709;
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
