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

    private static float s_displayPaperWhiteNits = DisplaySettings.DefaultPaperWhite;
    private static float s_displayPeakBrightnessNits = DisplaySettings.DefaultPeakBrightness;
    private static PostProcessDebugView s_debugView;
    private static float s_compareSplit;
    private static CompareMode s_compareMode;
    private static bool s_compareBefore;
    private static bool s_bypassPostProcessEffects;
    private static bool s_temporaryBypass;

    internal static float DisplayPaperWhiteNits => s_displayPaperWhiteNits;

    internal static float DisplayPeakBrightnessNits => s_displayPeakBrightnessNits;

    // Цветокоррекция кадра — один LUT. Присылает его сервер как эффект;
    // владеет объектом тот, кто его передал, и он же его освобождает.
    internal static ColorGradeCubeLUT? LUT { get; private set; }

    internal static float LUTIntensity { get; private set; }

    internal static ColorGradeLUTColorSpace LUTColorSpace { get; private set; }

    public static bool BypassPostProcessEffects
    {
        get => s_bypassPostProcessEffects;
        set
        {
            if (s_bypassPostProcessEffects == value)
            {
                return;
            }

            s_bypassPostProcessEffects = value;
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
        get => s_temporaryBypass;
        set
        {
            if (s_temporaryBypass == value)
            {
                return;
            }

            s_temporaryBypass = value;
        }
    }

    public static PostProcessDebugView DebugView
    {
        get => s_debugView;
        set
        {
            PostProcessDebugView sanitized = Enum.IsDefined(typeof(PostProcessDebugView), value)
                ? value
                : PostProcessDebugView.None;
            if (s_debugView == sanitized)
            {
                return;
            }

            s_debugView = sanitized;
        }
    }

    public static float CompareSplit
    {
        get => s_compareSplit;
        set
        {
            float sanitized = float.IsNaN(value) || float.IsInfinity(value)
                ? 0f
                : Mathf.Clamp01(value);
            if (Mathf.Approximately(s_compareSplit, sanitized))
            {
                return;
            }

            s_compareSplit = sanitized;
        }
    }

    public static CompareMode CompareMode
    {
        get => s_compareMode;
        set
        {
            CompareMode sanitized = Enum.IsDefined(typeof(CompareMode), value)
                ? value
                : CompareMode.Off;
            if (s_compareMode == sanitized)
            {
                return;
            }

            s_compareMode = sanitized;
        }
    }

    public static bool CompareBefore
    {
        get => s_compareBefore;
        set
        {
            if (s_compareBefore == value)
            {
                return;
            }

            s_compareBefore = value;
        }
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetForDomainReload()
    {
        MainCamera = null;
        DiagnosticOffscreenCamera = null;
        DiagnosticWorldImage = null;
        DiagnosticBloomImage = null;
        DiagnosticFullResolutionWorld = false;
        DiagnosticBloomDispatches = 0;
        DiagnosticBloomFrame = -1;
        DiagnosticSceneFrame = -1;
        DiagnosticDisplayFrame = -1;
        s_displayPaperWhiteNits = DisplaySettings.DefaultPaperWhite;
        s_displayPeakBrightnessNits = DisplaySettings.DefaultPeakBrightness;
        LUT = null;
        LUTIntensity = 0f;
        LUTColorSpace = ColorGradeLUTColorSpace.LinearRec709;
        s_debugView = PostProcessDebugView.None;
        s_compareSplit = 0f;
        s_compareMode = CompareMode.Off;
        s_compareBefore = false;
        s_bypassPostProcessEffects = false;
        s_temporaryBypass = false;
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
        if (Mathf.Approximately(s_displayPaperWhiteNits, sanitizedPaperWhite) &&
            Mathf.Approximately(s_displayPeakBrightnessNits, sanitizedPeakBrightness))
        {
            return;
        }

        s_displayPaperWhiteNits = sanitizedPaperWhite;
        s_displayPeakBrightnessNits = sanitizedPeakBrightness;
    }

    // null или нулевая сила выключают LUT; проход вывода тогда не тратит на
    // него ни чтения.
    public static void SetLUT(
        ColorGradeCubeLUT? lut,
        float intensity,
        ColorGradeLUTColorSpace colorSpace = ColorGradeLUTColorSpace.LinearRec709)
    {
        float sanitized = FiniteClamp(intensity, 0f, 1f, 0f);
        LUT = sanitized > 0f ? lut : null;
        LUTIntensity = LUT == null ? 0f : sanitized;
        LUTColorSpace = colorSpace is ColorGradeLUTColorSpace.LinearRec709 or ColorGradeLUTColorSpace.SrgbRec709
            ? colorSpace
            : ColorGradeLUTColorSpace.LinearRec709;
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
