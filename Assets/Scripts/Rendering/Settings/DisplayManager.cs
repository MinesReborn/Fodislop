#nullable enable

using System;
using System.Collections.Generic;
using System.Text;
using Kern.Core;
using Kern.Core.Interfaces;
using Kern.Rendering.PostProcessing;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using VContainer.Unity;

namespace Kern.Rendering
{
    // Чистый сервис контейнера (SCENE_STANDARD.md §1): настройки вывода
    // применяются при старте scope.
    public sealed class DisplayManager : IStartable
    {
        private readonly IClientConfigManager _clientConfig;
        private readonly IGameplayCamera _gameplayCamera;

        public DisplayManager(IClientConfigManager clientConfig, IGameplayCamera gameplayCamera)
        {
            _clientConfig = clientConfig;
            _gameplayCamera = gameplayCamera;
        }

        void IStartable.Start()
        {
            ApplyDisplaySettings();
        }

        public static void ApplyInitialSettings(DisplaySettings display)
        {
            if (display == null)
            {
                return;
            }

            HDROutput.SetEnabled(display.HDREnabled);
            AutoDetectDisplayCapabilities(display);
            SanitizeCalibration(display);
            PostProcessRuntimeState.SetDisplayCalibration(
                display.PaperWhiteNits,
                display.PeakBrightnessNits);

            ApplyFrameTiming(display);

            if (display.ResolutionWidth > 0 && display.ResolutionHeight > 0)
            {
                var mode = NormalizeFullScreenMode((FullScreenMode)display.FullScreenMode);
                int refresh = display.RefreshRate > 0 ? display.RefreshRate : (int)Screen.currentResolution.refreshRateRatio.value;
                Screen.SetResolution(display.ResolutionWidth, display.ResolutionHeight, mode, new RefreshRate { numerator = (uint)Mathf.Max(1, refresh), denominator = 1 });
            }
        }

        public static void ApplyFrameTiming(DisplaySettings display)
        {
            if (display == null)
            {
                return;
            }

            QualitySettings.vSyncCount = display.VSync ? 1 : 0;
            Application.targetFrameRate = display.TargetFrameRate;
            Time.maximumDeltaTime = 0.1f;
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

        private static void ApplyPixelSampling(PixelSamplingMode mode)
        {
            Shader.SetGlobalFloat(
                _PixelArtFilteringProperty,
                PixelSamplingRules.FiltersTexelEdges(mode) ? 1f : 0f);
        }

        private static readonly int _PixelArtFilteringProperty = Shader.PropertyToID("_PixelArtFiltering");

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

        public HDROutput.ApplyRequestResult SetHDREnabled(bool enabled)
        {
            if (_clientConfig?.Config == null)
            {
                return HDROutput.ApplyRequestResult.RejectedUnsupported;
            }

            _clientConfig.UpdateSection(config => config.Display, display => display.HDREnabled = enabled);

            HDROutput.ApplyRequestResult result = HDROutput.SetEnabled(enabled);
            if (result == HDROutput.ApplyRequestResult.RejectedNotSwitchable)
            {
                Debug.LogWarning(
                    "[HDR] The current output cannot switch HDR at runtime; " +
                    $"the preference is kept at {enabled} for a compatible output.");
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

        public void SetPaperWhiteNits(float paperWhiteNits)
        {
            if (_clientConfig?.Config == null)
            {
                return;
            }

            float sanitizedPaperWhite = FiniteClamp(
                paperWhiteNits,
                DisplaySettings.PaperWhiteMin,
                DisplaySettings.PaperWhiteMax,
                DisplaySettings.DefaultPaperWhite);
            float sanitizedPeak = Mathf.Max(
                sanitizedPaperWhite,
                FiniteClamp(
                    _clientConfig.Config.Display.PeakBrightnessNits,
                    DisplaySettings.PeakBrightnessMin,
                    DisplaySettings.PeakBrightnessMax,
                    DisplaySettings.DefaultPeakBrightness));
            _clientConfig.UpdateSection(config => config.Display, display =>
            {
                display.PaperWhiteNits = sanitizedPaperWhite;
                display.PeakBrightnessNits = sanitizedPeak;
            });
            PostProcessRuntimeState.SetDisplayCalibration(
                sanitizedPaperWhite,
                sanitizedPeak);
            Debug.Log(
                $"[DisplayManager] SetPaperWhiteNits: {sanitizedPaperWhite} " +
                $"(Peak={sanitizedPeak})");
        }

        public void SetPeakBrightnessNits(float peakBrightnessNits)
        {
            if (_clientConfig?.Config == null)
            {
                return;
            }

            float paperWhite = FiniteClamp(
                _clientConfig.Config.Display.PaperWhiteNits,
                DisplaySettings.PaperWhiteMin,
                DisplaySettings.PaperWhiteMax,
                DisplaySettings.DefaultPaperWhite);
            float sanitizedPeak = Mathf.Max(
                paperWhite,
                FiniteClamp(
                    peakBrightnessNits,
                    DisplaySettings.PeakBrightnessMin,
                    DisplaySettings.PeakBrightnessMax,
                    DisplaySettings.DefaultPeakBrightness));
            _clientConfig.UpdateSection(config => config.Display, display =>
            {
                display.PaperWhiteNits = paperWhite;
                display.PeakBrightnessNits = sanitizedPeak;
            });
            PostProcessRuntimeState.SetDisplayCalibration(
                paperWhite,
                sanitizedPeak);
            Debug.Log($"[DisplayManager] SetPeakBrightnessNits: {sanitizedPeak}");
        }

        public static void AutoDetectDisplayCapabilities(DisplaySettings display)
        {
            HDROutput.AutoDetectDisplayCapabilities(display);
        }

        private static void SanitizeCalibration(DisplaySettings display)
        {
            display.PaperWhiteNits = FiniteClamp(
                display.PaperWhiteNits,
                DisplaySettings.PaperWhiteMin,
                DisplaySettings.PaperWhiteMax,
                DisplaySettings.DefaultPaperWhite);
            display.PeakBrightnessNits = Mathf.Max(
                display.PaperWhiteNits,
                FiniteClamp(
                    display.PeakBrightnessNits,
                    DisplaySettings.PeakBrightnessMin,
                    DisplaySettings.PeakBrightnessMax,
                    DisplaySettings.DefaultPeakBrightness));
        }

        private static float FiniteClamp(
            float value,
            float minimum,
            float maximum,
            float fallback) =>
            float.IsNaN(value) || float.IsInfinity(value)
                ? fallback
                : Mathf.Clamp(value, minimum, maximum);

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
    }
}
