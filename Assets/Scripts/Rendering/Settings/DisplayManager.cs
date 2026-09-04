#nullable enable

using System;
using System.Collections.Generic;
using System.Text;
using Fodinae.Core;
using Fodinae.Core.Interfaces;
using Fodinae.Rendering.PostProcessing;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using VContainer;

namespace Fodinae.Rendering
{
    public class DisplayManager : MonoBehaviour
    {
        [Inject]
        private IClientConfigManager _clientConfig = null!;
        [Inject]
        private IGameplayCamera _gameplayCamera = null!;

        protected void Start()
        {
            ApplyDisplaySettings();
        }

        public static void ApplyInitialSettings(DisplaySettings display)
        {
            if (display == null)
            {
                return;
            }

            AutoDetectDisplayCapabilities(display);
            HDROutput.SetEnabled(display.HDREnabled);
            PostProcessRenderPass.SetDisplayCalibration(
                display.Gamma,
                display.PaperWhiteNits,
                display.PeakBrightnessNits);

            QualitySettings.vSyncCount = display.VSync ? 1 : 0;
            Application.targetFrameRate = display.TargetFrameRate;
            Time.maximumDeltaTime = 0.1f;

            if (display.ResolutionWidth > 0 && display.ResolutionHeight > 0)
            {
                var mode = NormalizeFullScreenMode((FullScreenMode)display.FullScreenMode);
                int refresh = display.RefreshRate > 0 ? display.RefreshRate : (int)Screen.currentResolution.refreshRateRatio.value;
                Screen.SetResolution(display.ResolutionWidth, display.ResolutionHeight, mode, new RefreshRate { numerator = (uint)Mathf.Max(1, refresh), denominator = 1 });
            }
        }

        public void ApplyDisplaySettings()
        {
            if (_clientConfig?.Config == null)
            {
                return;
            }

            DisplaySettings display = _clientConfig.Config.Display;
            ApplyInitialSettings(display);
            ApplyPixelSampling(display.PixelSampling);
            HDROutput.ConfigureCamera(_gameplayCamera.Camera);
        }

        /// <summary>
        /// Переключает режим укладки мира на пиксельную сетку.
        /// </summary>
        public void SetPixelSamplingMode(PixelSamplingMode mode)
        {
            if (_clientConfig?.Config == null)
            {
                return;
            }

            _clientConfig.UpdateSection(config => config.Display, display => display.PixelSampling = mode);
            ApplyPixelSampling(mode);
            Debug.Log($"[DisplayManager] SetPixelSamplingMode: {mode}");
        }

        /// <summary>
        /// Раздаёт режим шейдерам.
        /// </summary>
        /// <remarks>
        /// Через глобальную переменную шейдера, а не через материалы:
        /// террейн и сущности мира рисуются разными материалами, часть из
        /// которых создаётся в рантайме, и обойти их все означало бы
        /// завести реестр материалов ради одного тумблера.
        ///
        /// Камера читает режим сама: ей нужен не флаг, а решение, округлять
        /// ли размер, и это её собственная работа.
        /// </remarks>
        private static void ApplyPixelSampling(PixelSamplingMode mode)
        {
            Shader.SetGlobalFloat(
                PixelArtFilteringProperty,
                mode == PixelSamplingMode.SmoothFiltered ? 1f : 0f);
        }

        private static readonly int PixelArtFilteringProperty = Shader.PropertyToID("_PixelArtFiltering");

        public void SetResolution(int width, int height, FullScreenMode mode, int refreshRate = 60)
        {
            if (_clientConfig?.Config == null)
            {
                return;
            }

            mode = NormalizeFullScreenMode(mode);
            _clientConfig.UpdateSection(config => config.Display, display =>
            {
                display.ResolutionWidth = width;
                display.ResolutionHeight = height;
                display.FullScreenMode = (int)mode;
                display.RefreshRate = refreshRate;
            });

            Screen.SetResolution(width, height, mode, new RefreshRate { numerator = (uint)Mathf.Max(1, refreshRate), denominator = 1 });
            Debug.Log($"[DisplayManager] SetResolution: {width}x{height} @ {refreshRate}Hz (Mode={mode})");
        }

        public void SetVSync(bool enabled)
        {
            if (_clientConfig?.Config == null)
            {
                return;
            }

            _clientConfig.UpdateSection(config => config.Display, display => display.VSync = enabled);

            QualitySettings.vSyncCount = enabled ? 1 : 0;
            Application.targetFrameRate = _clientConfig.Config.Display.TargetFrameRate;
            Debug.Log($"[DisplayManager] SetVSync: {enabled} (TargetFPS={_clientConfig.Config.Display.TargetFrameRate})");
        }

        /// <summary>
        /// Applies the HDR preference and reports what the display did with it.
        /// </summary>
        /// <remarks>
        /// A refused request must not stay written in the config. Otherwise the
        /// settings toggle keeps reading back "on" from a preference the display
        /// never honoured, and the player is told the opposite of what they see.
        /// The one refusal that is NOT rolled back is an absent HDR display:
        /// availability is reported late and can appear after a monitor change,
        /// and HDROutputReconciler completes the request when it does.
        /// </remarks>
        public HDROutput.ApplyRequestResult SetHDREnabled(bool enabled)
        {
            if (_clientConfig?.Config == null)
            {
                return HDROutput.ApplyRequestResult.RejectedUnsupported;
            }

            bool previous = _clientConfig.Config.Display.HDREnabled;
            _clientConfig.UpdateSection(config => config.Display, display => display.HDREnabled = enabled);

            HDROutput.ApplyRequestResult result = HDROutput.SetEnabled(enabled);
            if (result == HDROutput.ApplyRequestResult.RejectedNotSwitchable)
            {
                _clientConfig.UpdateSection(config => config.Display, display => display.HDREnabled = previous);
                Debug.LogWarning(
                    "[HDR] Display is HDR-capable but not runtime-switchable; " +
                    $"the preference stays at {previous}. Switch HDR in the OS display settings.");
                return result;
            }

            if (result == HDROutput.ApplyRequestResult.RejectedUnsupported)
            {
                Debug.LogWarning(
                    "[HDR] No HDR-capable display is reported yet; the preference is kept " +
                    "and applied by HDROutputReconciler once one appears.");
            }

            HDROutput.ConfigureCamera(_gameplayCamera.Camera);
            Debug.Log($"[DisplayManager] SetHDREnabled: {enabled} (Result={result})");
            return result;
        }

        public IReadOnlyList<Resolution> GetSupportedResolutions()
        {
            return Screen.resolutions;
        }

        public void SetGamma(float gamma)
        {
            if (_clientConfig?.Config == null)
            {
                return;
            }

            _clientConfig.UpdateSection(config => config.Display, display => display.Gamma = gamma);
            PostProcessRenderPass.SetDisplayCalibration(
                gamma,
                _clientConfig.Config.Display.PaperWhiteNits,
                _clientConfig.Config.Display.PeakBrightnessNits);
            Debug.Log($"[DisplayManager] SetGamma: {gamma}");
        }

        public void SetPaperWhiteNits(float paperWhiteNits)
        {
            if (_clientConfig?.Config == null)
            {
                return;
            }

            _clientConfig.UpdateSection(config => config.Display, display => display.PaperWhiteNits = paperWhiteNits);
            PostProcessRenderPass.SetDisplayCalibration(
                _clientConfig.Config.Display.Gamma,
                paperWhiteNits,
                _clientConfig.Config.Display.PeakBrightnessNits);
            Debug.Log($"[DisplayManager] SetPaperWhiteNits: {paperWhiteNits}");
        }

        public void SetPeakBrightnessNits(float peakBrightnessNits)
        {
            if (_clientConfig?.Config == null)
            {
                return;
            }

            _clientConfig.UpdateSection(config => config.Display, display => display.PeakBrightnessNits = peakBrightnessNits);
            PostProcessRenderPass.SetDisplayCalibration(
                _clientConfig.Config.Display.Gamma,
                _clientConfig.Config.Display.PaperWhiteNits,
                peakBrightnessNits);
            Debug.Log($"[DisplayManager] SetPeakBrightnessNits: {peakBrightnessNits}");
        }

        public static void AutoDetectDisplayCapabilities(DisplaySettings display)
        {
            HDROutputSettings output = HDROutputSettings.main;
            if (output.available)
            {
                if (display.PaperWhiteNits <= 10f && output.paperWhiteNits > 10f)
                {
                    display.PaperWhiteNits = output.paperWhiteNits;
                }

                if (display.PeakBrightnessNits <= 100f && output.maxToneMapLuminance > 100)
                {
                    display.PeakBrightnessNits = (float)output.maxToneMapLuminance;
                }
            }
        }

        /// <summary>
        /// Unity на macOS не поддерживает ExclusiveFullScreen — единственный
        /// полноэкранный режим там FullScreenWindow. Маппим до вызова
        /// Screen.SetResolution, чтобы конфиг «exclusive» не ронял окно на Mac.
        /// </summary>
        private static FullScreenMode NormalizeFullScreenMode(FullScreenMode mode)
        {
#if UNITY_STANDALONE_OSX
            return mode == FullScreenMode.ExclusiveFullScreen
                ? FullScreenMode.FullScreenWindow
                : mode;
#else
            return mode;
#endif
        }

        /// <summary>
        /// Owns the boundary between the scene-linear HDR render and the operating
        /// system's HDR display surface.
        /// </summary>
        public static class HDROutput
        {
            private static HDRDiagnosticState _lastDiagnosticState;
            private static bool _hasDiagnosticState;
            private static bool _enabled;
            private static bool _preferenceInitialized;

            public static bool Enabled => _preferenceInitialized && _enabled;

            private readonly record struct HDRDiagnosticState(
                bool Available,
                bool Active,
                bool ChangeRequested,
                HDRDisplaySupportFlags SupportFlags,
                ColorGamut Gamut,
                float PaperWhiteNits,
                int MinToneMapLuminance,
                int MaxToneMapLuminance);

            [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
            private static void ResetDiagnostics()
            {
                _lastDiagnosticState = default;
                _hasDiagnosticState = false;
                _enabled = false;
                _preferenceInitialized = false;
            }

            public enum ApplyRequestResult
            {
                /// <summary>Запрос применён, дисплей поставлен в режим <c>enabled</c>.</summary>
                Applied,
                /// <summary>Запрос отправлен ранее и ещё в полёте; повторный вызов проигнорирован.</summary>
                AlreadyPending,
                /// <summary>Дисплей не HDR-capable в принципе (нет <c>HDROutputSettings.available</c>).</summary>
                RejectedUnsupported,
                /// <summary>Дисплей HDR-capable, но без <c>RuntimeSwitchable</c> флага — переключение невозможно.</summary>
                RejectedNotSwitchable,
            }

            public static ApplyRequestResult SetEnabled(bool enabled)
            {
                // Store intent before probing the display. Availability can be
                // reported late (for example after a scene or display change),
                // and Refresh must still be able to complete the request.
                _enabled = enabled;
                _preferenceInitialized = true;

                HDROutputSettings output = HDROutputSettings.main;
                if (!output.available)
                {
                    LogDiagnostics(output);
                    return ApplyRequestResult.RejectedUnsupported;
                }

                if (!output.HDRModeChangeRequested && enabled == output.active)
                {
                    LogDiagnostics(output);
                    return ApplyRequestResult.Applied;
                }

                bool runtimeSwitchable =
                    (SystemInfo.hdrDisplaySupportFlags &
                        HDRDisplaySupportFlags.RuntimeSwitchable) != 0;
                if (!runtimeSwitchable)
                {
                    LogDiagnostics(output);
                    return ApplyRequestResult.RejectedNotSwitchable;
                }

                if (output.HDRModeChangeRequested)
                {
                    LogDiagnostics(output);
                    return ApplyRequestResult.AlreadyPending;
                }

                // Request a switch only when the current state differs from
                // the user request, otherwise we keep spamming
                // RequestHDRModeChange every toggle reset.
                if (enabled != output.active)
                {
                    output.RequestHDRModeChange(enabled);
                }

                LogDiagnostics(output);
                return ApplyRequestResult.Applied;
            }

            public static void Reconcile()
            {
                HDROutputSettings output = HDROutputSettings.main;
                if (!output.available)
                {
                    LogDiagnostics(output);
                    return;
                }

                if (_preferenceInitialized && Enabled != output.active &&
                    (SystemInfo.hdrDisplaySupportFlags &
                        HDRDisplaySupportFlags.RuntimeSwitchable) != 0 &&
                    !output.HDRModeChangeRequested)
                {
                    output.RequestHDRModeChange(Enabled);
                }

                LogDiagnostics(output);
            }

            private static void LogDiagnostics(HDROutputSettings output)
            {
                bool available = output.available;
                var state = new HDRDiagnosticState(
                    available,
                    available && output.active,
                    available && output.HDRModeChangeRequested,
                    SystemInfo.hdrDisplaySupportFlags,
                    available ? output.displayColorGamut : default,
                    available ? output.paperWhiteNits : 0f,
                    available ? output.minToneMapLuminance : 0,
                    available ? output.maxToneMapLuminance : 0);
                if (_hasDiagnosticState && state == _lastDiagnosticState)
                {
                    return;
                }

                _lastDiagnosticState = state;
                _hasDiagnosticState = true;
                Debug.Log(
                    "[HDR] " +
                    $"available={state.Available}, active={state.Active}, " +
                    $"changeRequested={state.ChangeRequested}, " +
                    $"supportFlags={state.SupportFlags}, gamut={state.Gamut}, " +
                    $"paperWhite={state.PaperWhiteNits:F1} nits, " +
                    $"min={state.MinToneMapLuminance} nits, " +
                    $"max={state.MaxToneMapLuminance} nits.");
            }

            public static void AppendDebugInfo(StringBuilder builder, Camera? camera)
            {
                if (builder == null)
                {
                    throw new ArgumentNullException(nameof(builder));
                }

                HDROutputSettings output = HDROutputSettings.main;
                bool available = output.available;
                bool active = available && output.active;
                bool changeRequested = available && output.HDRModeChangeRequested;
                ColorGamut gamut = available ? output.displayColorGamut : default;
                float paperWhiteNits = available ? output.paperWhiteNits : 0f;
                int minNits = available ? output.minToneMapLuminance : 0;
                int maxNits = available ? output.maxToneMapLuminance : 0;
                string status = !Enabled
                    ? "DISABLED"
                    : active
                        ? "ACTIVE"
                        : available ? "AVAILABLE / INACTIVE" : "UNAVAILABLE";
                builder.Append("<b>[HDR: ").Append(status).Append("]</b>\n")
                    .Append("Enabled in settings: ").Append(Enabled).Append('\n')
                    .Append("Available: ").Append(available)
                    .Append(" | Active: ").Append(active)
                    .Append(" | Requested: ").Append(changeRequested).Append('\n')
                    .Append("Support: ").Append(SystemInfo.hdrDisplaySupportFlags)
                    .Append(" | Gamut: ").Append(gamut).Append('\n')
                    .Append("Luminance: ").Append(minNits)
                    .Append(" / ").Append(paperWhiteNits.ToString("F1"))
                    .Append(" / ").Append(maxNits)
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
                        .Append(cameraData.renderPostProcessing ? "ON (!)" : "OFF (custom only)");
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

                camera.allowHDR = true;
                if (camera.TryGetComponent(out UniversalAdditionalCameraData cameraData))
                {
                    HDROutputSettings output = HDROutputSettings.main;

                    // Only enable HDR on the camera if the user enabled it in settings
                    // AND the connected display actually supports and runs HDR.
                    cameraData.allowHDROutput = Enabled &&
                        output.available && output.active;

                    // Fodinae has one post-processing chain: the custom
                    // renderer feature. URP FinalBlit still performs the
                    // mandatory display color-space conversion and transfer
                    // encoding; that output step is not a second PP stack.
                    cameraData.renderPostProcessing = false;
                }
            }

        }
    }
}
