#nullable enable

namespace Kern.Rendering;

using System;
using System.Text;
using Kern.Core;
using Kern.Core.Interfaces;
using Kern.Rendering.PostProcessing;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

public static class HDROutput
{
    private static HDRDiagnosticState s_lastDiagnosticState;
    private static bool s_hasDiagnosticState;
    private static string? s_lastReadError;
    private static HDROutputController s_controller = new(new UnityHDROutputBackend());

    public static bool Enabled => s_controller.DesiredHDR;

    public static bool Active => s_controller.Current.RenderingHDR;
    public static HDROutputController.Phase Status => s_controller.Status;
    public static bool RuntimeSwitchable => s_controller.Current.Switchable;
    public static bool CanSwitch => s_controller.Current.CanSwitch &&
        s_controller.Status is not HDROutputController.Phase.Pending and not HDROutputController.Phase.Uninitialized &&
        !s_controller.HasReadFailure;

    public static bool CanRetryRead => s_controller.HasReadFailure;

    // Ключ дедупликации строится по решениям, а не по снимку целиком.
    // Снимок несёт paperWhiteNits дробным числом от системы: оно дрожит в
    // младших разрядах само по себе, и сравнение снимков печатало новую
    // строку каждую секунду, хотя состояние вывода не менялось. Яркости
    // входят округлёнными до нита — на уровне решения различать тоньше
    // нечего, а реальную смену калибровки такой ключ всё ещё ловит.
    private readonly record struct HDRDiagnosticState(
        HDROutputController.OutputIdentity Identity,
        bool Supported,
        bool PipelineSupported,
        bool Available,
        bool Active,
        bool Pending,
        bool Switchable,
        int PaperWhiteNits,
        int MinNits,
        int MaxNits,
        int Gamut,
        bool DesiredHDR,
        HDROutputController.Phase Phase,
        int Attempts,
        string? Error)
    {
        public static HDRDiagnosticState From(
            HDROutputController.Snapshot output,
            bool desiredHDR,
            HDROutputController.Phase phase,
            int attempts,
            string? error) =>
            new(output.Identity, output.Supported, output.PipelineSupported, output.Available,
                output.Active, output.Pending, output.Switchable,
                Mathf.RoundToInt(output.PaperWhiteNits), output.MinNits, output.MaxNits, output.Gamut,
                desiredHDR, phase, attempts, error);
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetDiagnostics()
    {
        s_lastDiagnosticState = default;
        s_hasDiagnosticState = false;
        s_controller = new HDROutputController(new UnityHDROutputBackend());
    }

    public enum ApplyRequestResult
    {
        Applied,

        Requested,

        AlreadyPending,

        RejectedUnsupported,

        RejectedNotSwitchable,

        Retrying,
        Failed,
    }

    public static void AutoDetectDisplayCapabilities(DisplaySettings display)
    {
        HDROutputController.Snapshot output = s_controller.Current;
        if (output.Available)
        {
            if (display.PaperWhiteNits <= 10f && output.PaperWhiteNits > 10f)
            {
                display.PaperWhiteNits = Mathf.Clamp(
                    output.PaperWhiteNits,
                    DisplaySettings.PaperWhiteMin,
                    DisplaySettings.PaperWhiteMax);
            }

            if (display.PeakBrightnessNits <= 100f && output.MaxNits > 100)
            {
                display.PeakBrightnessNits = Mathf.Max(
                    display.PaperWhiteNits,
                    Mathf.Clamp(
                        output.MaxNits,
                        DisplaySettings.PeakBrightnessMin,
                        DisplaySettings.PeakBrightnessMax));
            }
        }
    }

    public static ApplyRequestResult SetEnabled(bool enabled)
    {
        s_controller.SetPreference(enabled);

        return ApplyPreference();
    }

    public static void RetryRead()
    {
        s_controller.RetryRead();
        Reconcile();
    }

    public static void Retry()
    {
        s_controller.NotifyEnvironmentChanged();
        Reconcile();
    }

    public static void Reconcile()
    {
        ApplyPreference();
    }

    private static ApplyRequestResult ApplyPreference()
    {
        int attempts = s_controller.Attempts;
        s_controller.Update(Time.realtimeSinceStartupAsDouble);
        LogDiagnostics();
        return s_controller.Status switch
        {
            HDROutputController.Phase.HDR or HDROutputController.Phase.SDR => ApplyRequestResult.Applied,
            HDROutputController.Phase.Pending => s_controller.Attempts > attempts
                ? ApplyRequestResult.Requested : ApplyRequestResult.AlreadyPending,
            HDROutputController.Phase.NotSwitchable => ApplyRequestResult.RejectedNotSwitchable,
            HDROutputController.Phase.Retrying => ApplyRequestResult.Retrying,
            HDROutputController.Phase.Failed => ApplyRequestResult.Failed,
            _ => ApplyRequestResult.RejectedUnsupported,
        };
    }

    private static void LogDiagnostics()
    {
        // Пока предпочтение не задано, докладывать не о чем: контроллер
        // прочитал вывод, но ни одного решения не принял. Такое состояние
        // существует ровно между стартом HdrOutputReconciler и применением
        // настроек дисплея — порядок IStartable не определён, и оба пути
        // проходят здесь. Строка про desired=False/Uninitialized в этом окне
        // не отчёт, а внутренний порядок запуска, вынесенный в лог: читается
        // как второе включение HDR, которого не было.
        if (Status == HDROutputController.Phase.Uninitialized)
        {
            return;
        }

        HDROutputController.Snapshot output = s_controller.Current;
        HDRDiagnosticState state = HDRDiagnosticState.From(
            output, Enabled, Status, s_controller.Attempts, s_controller.Error);
        string? readError = s_controller.Error;

        // A read failure is terminal until the user retries it. The same
        // failure recurs every reconcile/tick while it stands, and the two
        // startup paths (DisplayManager.ApplyInitialSettings vs
        // HdrOutputReconciler.Start) initialize with different DesiredHDR, so
        // the state differs and the warning would otherwise fire twice.
        if (readError != null)
        {
            if (readError == s_lastReadError)
            {
                return;
            }

            s_lastReadError = readError;
        }
        else
        {
            s_lastReadError = null;
        }

        if (s_hasDiagnosticState && state == s_lastDiagnosticState)
        {
            return;
        }

        s_lastDiagnosticState = state;
        s_hasDiagnosticState = true;
        string message =
            "[HDR] " +
            $"available={output.Available}, active={output.Active}, " +
            $"changeRequested={output.Pending}, " +
            $"display={output.Identity}, desired={state.DesiredHDR}, phase={state.Phase}, " +
            $"attempts={state.Attempts}, error={state.Error}, " +
            $"environment={Application.platform}, graphicsAPI={SystemInfo.graphicsDeviceType}, " +
            $"pipelineHDR={output.PipelineSupported}, " +
            $"supported={output.Supported}, switchable={output.Switchable}, " +
            $"gamut={(ColorGamut)output.Gamut}, " +
            $"paperWhite={output.PaperWhiteNits:F1} nits, " +
            $"min={output.MinNits} nits, " +
            $"max={output.MaxNits} nits.";
        if (state.Phase == HDROutputController.Phase.Failed)
        {
            Debug.LogWarning(message);
        }
        else
        {
            Debug.Log(message);
        }
    }

    public static void AppendDebugInfo(StringBuilder builder, Camera? camera)
    {
        if (builder == null)
        {
            throw new ArgumentNullException(nameof(builder));
        }

        HDROutputController.Snapshot output = s_controller.Current;
        string status = Status.ToString();
        builder.Append("<b>[HDR: ").Append(status).Append("]</b>\n")
            .Append("Window display: ").Append(output.Identity.Name)
            .Append(" | Environment: ").Append(Application.platform)
            .Append(" | Graphics API: ").Append(SystemInfo.graphicsDeviceType).Append('\n')
            .Append("Enabled in settings: ").Append(Enabled).Append('\n')
            .Append("Attempts: ").Append(s_controller.Attempts)
            .Append(" | Error: ").Append(s_controller.Error ?? "none").Append('\n')
            .Append("Available: ").Append(output.Available)
            .Append(" | Active: ").Append(output.Active)
            .Append(" | Requested: ").Append(output.Pending).Append('\n')
            .Append("Supported: ").Append(output.Supported)
            .Append(" | Pipeline HDR: ").Append(output.PipelineSupported)
            .Append(" | Switchable: ").Append(output.Switchable)
            .Append(" | Gamut: ").Append((ColorGamut)output.Gamut).Append('\n')
            .Append("Luminance: ").Append(output.MinNits)
            .Append(" / ").Append(output.PaperWhiteNits.ToString("F1"))
            .Append(" / ").Append(output.MaxNits)
            .Append(" nits (min / paper / OS max)\n");

        if (camera == null)
        {
            builder.Append("Display camera: MISSING\n\n");
            return;
        }

        builder.Append("Camera HDR buffer: ").Append(camera.allowHDR);
        if (camera.TryGetComponent(out UniversalAdditionalCameraData cameraData))
        {
            builder.Append(" | HDR output: ").Append(cameraData.allowHDROutput)
                .Append(" | Unity PP: ")
                .Append(cameraData.renderPostProcessing ? "ON (URP output)" : "OFF");
        }
        else
        {
            builder.Append(" | URP camera data: MISSING");
        }

        builder.Append("\n\n");
    }

    public static void ConfigureCamera(Camera camera)
    {
        // HDR output belongs only to cameras resolving to a
        // display. Enabling it on an offscreen RenderTexture camera can
        // invalidate that camera's explicitly authored LDR target path.
        if (camera.targetTexture != null)
        {
            return;
        }

        // Вызывается раз в секунду: присваивание только при расхождении,
        // чтобы проверка не трогала камеру, когда менять нечего.
        if (!camera.allowHDR)
        {
            camera.allowHDR = true;
        }

        if (camera.TryGetComponent(out UniversalAdditionalCameraData cameraData))
        {
            // This is permission, not a cached copy of the swapchain state.
            // URP checks the live output state when constructing camera data.
            if (!cameraData.allowHDROutput)
            {
                cameraData.allowHDROutput = true;
            }

            // Художественные эффекты идут до URP. Тонмаппинг, праймари дисплея,
            // композиция UI и кодирование вывода — за URP, как и дизеринг: он
            // кладёт шум на 8-битный код уже после перевода в sRGB, то есть
            // ровно там, где градиент квантуется. Без постобработки камеры URP
            // пропускает финальный проход вместе с дизерингом.
            bool renderPostProcessing = !PostProcessRuntimeState.SkipPasses;
            if (cameraData.renderPostProcessing != renderPostProcessing)
            {
                cameraData.renderPostProcessing = renderPostProcessing;
            }

            if (!cameraData.dithering)
            {
                cameraData.dithering = true;
            }
        }
    }
}
