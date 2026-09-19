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
    private static HDRDiagnosticState _lastDiagnosticState;
    private static bool _hasDiagnosticState;
    private static string? _lastReadError;
    private static HDROutputController _controller = new(new UnityHDROutputBackend());

    public static bool Enabled => _controller.DesiredHDR;

    public static bool Active => _controller.Current.RenderingHDR;
    public static HDROutputController.Phase Status => _controller.Status;
    public static bool RuntimeSwitchable => _controller.Current.Switchable;
    public static bool CanSwitch => _controller.Current.CanSwitch &&
        _controller.Status is not HDROutputController.Phase.Pending and not HDROutputController.Phase.Uninitialized &&
        !_controller.HasReadFailure;

    public static bool CanRetryRead => _controller.HasReadFailure;

    private readonly record struct HDRDiagnosticState(
        HDROutputController.Snapshot Output,
        bool DesiredHDR,
        HDROutputController.Phase Phase,
        int Attempts,
        string? Error);

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetDiagnostics()
    {
        _lastDiagnosticState = default;
        _hasDiagnosticState = false;
        _controller = new HDROutputController(new UnityHDROutputBackend());
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
        HDROutputController.Snapshot output = _controller.Current;
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
        _controller.SetPreference(enabled);

        return ApplyPreference();
    }

    public static void RetryRead()
    {
        _controller.RetryRead();
        Reconcile();
    }

    public static void Retry()
    {
        _controller.NotifyEnvironmentChanged();
        Reconcile();
    }

    public static void Reconcile()
    {
        ApplyPreference();
    }

    private static ApplyRequestResult ApplyPreference()
    {
        int attempts = _controller.Attempts;
        _controller.Update(Time.realtimeSinceStartupAsDouble);
        LogDiagnostics();
        return _controller.Status switch
        {
            HDROutputController.Phase.HDR or HDROutputController.Phase.SDR => ApplyRequestResult.Applied,
            HDROutputController.Phase.Pending => _controller.Attempts > attempts
                ? ApplyRequestResult.Requested : ApplyRequestResult.AlreadyPending,
            HDROutputController.Phase.NotSwitchable => ApplyRequestResult.RejectedNotSwitchable,
            HDROutputController.Phase.Retrying => ApplyRequestResult.Retrying,
            HDROutputController.Phase.Failed => ApplyRequestResult.Failed,
            _ => ApplyRequestResult.RejectedUnsupported,
        };
    }

    private static void LogDiagnostics()
    {
        var state = new HDRDiagnosticState(
            _controller.Current, Enabled, Status, _controller.Attempts, _controller.Error);
        string? readError = _controller.Error;

        // A read failure is terminal until the user retries it. The same
        // failure recurs every reconcile/tick while it stands, and the two
        // startup paths (DisplayManager.ApplyInitialSettings vs
        // HDROutputReconciler.Start) initialize with different DesiredHDR, so
        // the state differs and the warning would otherwise fire twice.
        if (readError != null)
        {
            if (readError == _lastReadError)
            {
                return;
            }

            _lastReadError = readError;
        }
        else
        {
            _lastReadError = null;
        }

        if (_hasDiagnosticState && state == _lastDiagnosticState)
        {
            return;
        }

        _lastDiagnosticState = state;
        _hasDiagnosticState = true;
        string message =
            "[HDR] " +
            $"available={state.Output.Available}, active={state.Output.Active}, " +
            $"changeRequested={state.Output.Pending}, " +
            $"display={state.Output.Identity}, desired={state.DesiredHDR}, phase={state.Phase}, " +
            $"attempts={state.Attempts}, error={state.Error}, " +
            $"environment={Application.platform}, graphicsAPI={SystemInfo.graphicsDeviceType}, " +
            $"pipelineHDR={_controller.Current.PipelineSupported}, " +
            $"supported={state.Output.Supported}, switchable={state.Output.Switchable}, " +
            $"gamut={(ColorGamut)state.Output.Gamut}, " +
            $"paperWhite={state.Output.PaperWhiteNits:F1} nits, " +
            $"min={state.Output.MinNits} nits, " +
            $"max={state.Output.MaxNits} nits.";
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

        HDROutputController.Snapshot output = _controller.Current;
        string status = Status.ToString();
        builder.Append("<b>[HDR: ").Append(status).Append("]</b>\n")
            .Append("Window display: ").Append(output.Identity.Name)
            .Append(" | Environment: ").Append(Application.platform)
            .Append(" | Graphics API: ").Append(SystemInfo.graphicsDeviceType).Append('\n')
            .Append("Enabled in settings: ").Append(Enabled).Append('\n')
            .Append("Attempts: ").Append(_controller.Attempts)
            .Append(" | Error: ").Append(_controller.Error ?? "none").Append('\n')
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
