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
    // Чистый сервис контейнера (docs/architecture/SCENE_STANDARD.md §1): настройки вывода
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

        // Возвращает true, если калибровка в настройках была изменена —
        // автоопределением дисплея или починкой негодного значения. Вызывающий
        // обязан такое изменение сохранить: обе процедуры пишут прямо в объект
        // настроек, минуя UpdateSection, то есть без SaveDeferred. Раньше это
        // значило, что определённая яркость дисплея жила только до выхода из
        // игры, а NaN в файле чинился в памяти на каждом запуске и оставался
        // в файле навсегда.
        public static bool ApplyInitialSettings(DisplaySettings display)
        {
            if (display == null)
            {
                return false;
            }

            float previousPaperWhite = display.PaperWhiteNits;
            float previousPeak = display.PeakBrightnessNits;

            // Безопасный старт. Флаг переживает выход из игры и означает, что
            // в прошлый раз переключение режима вывода никто не подтвердил.
            // Единственное безопасное прочтение этого — экран после
            // переключения был нечитаем, поэтому запускаемся в SDR. Иначе
            // человек с несовместимым выводом крутится в чёрном экране: игра
            // каждый запуск честно применяет сохранённую настройку.
            bool abandonedSwitch = display.HDRSwitchPending;
            if (abandonedSwitch)
            {
                display.HDREnabled = false;
                display.HDRSwitchPending = false;
                Debug.LogWarning(
                    "[HDR] The previous session did not confirm a display mode switch; " +
                    "starting in SDR.");
            }

            HDROutput.SetEnabled(display.HDREnabled);
            AutoDetectDisplayCapabilities(display);
            SanitizeCalibration(display);

            // float.Equals, а не оператор ==: здесь важен сам факт записи,
            // включая починку NaN, который оператору не равен ничему, в том
            // числе самому себе, и такая замена осталась бы незамеченной.
            bool changed =
                abandonedSwitch ||
                !previousPaperWhite.Equals(display.PaperWhiteNits) ||
                !previousPeak.Equals(display.PeakBrightnessNits);

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

            return changed;
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
            // Начальные режимы экрана применяются ApplicationBootstrap до
            // shader warmup. Повторный вызов здесь заново читает HDR-состояние
            // и может запустить лишнюю попытку переключения Pending/Retrying.
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

        // Temporary renderer fixtures retain and restore their own dimensions;
        // they must not persist a benchmark resolution into client settings.
        internal static void SetTransientResolution(int width, int height, FullScreenMode mode)
        {
            if (width <= 0 || height <= 0)
            {
                throw new ArgumentOutOfRangeException(width <= 0 ? nameof(width) : nameof(height));
            }
            Screen.SetResolution(width, height, NormalizeFullScreenMode(mode));
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

            // Save, а не SaveDeferred: между этой строкой и подтверждением
            // экран может стать нечитаемым, и отложенная запись до диска не
            // доедет. Метка обязана лежать в файле раньше, чем сменится режим.
            _clientConfig.UpdateSection(config => config.Display, display =>
            {
                display.HDREnabled = enabled;
                display.HDRSwitchPending = true;
            });
            _clientConfig.Save();

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

        // Снимает метку безопасного старта: человек ответил на окно
        // подтверждения, значит экран читается. Вызывается по обоим исходам —
        // и когда режим оставили, и когда откатили: откат тоже переключение,
        // и незакрытая метка выключила бы HDR на следующем запуске зря.
        public void ConfirmHDRSwitchSeen()
        {
            if (_clientConfig?.Config == null || !_clientConfig.Config.Display.HDRSwitchPending)
            {
                return;
            }

            _clientConfig.UpdateSection(config => config.Display, display => display.HDRSwitchPending = false);
            _clientConfig.Save();
        }

        public void SetPaperWhiteNits(float paperWhiteNits)
        {
            if (_clientConfig?.Config == null)
            {
                return;
            }

            float sanitizedPaperWhite = QuantizeNits(FiniteClamp(
                paperWhiteNits,
                DisplaySettings.PaperWhiteMin,
                DisplaySettings.PaperWhiteMax,
                DisplaySettings.DefaultPaperWhite));
            float sanitizedPeak = Mathf.Max(
                sanitizedPaperWhite,
                QuantizeNits(FiniteClamp(
                    _clientConfig.Config.Display.PeakBrightnessNits,
                    DisplaySettings.PeakBrightnessMin,
                    DisplaySettings.PeakBrightnessMax,
                    DisplaySettings.DefaultPeakBrightness)));
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

            float paperWhite = QuantizeNits(FiniteClamp(
                _clientConfig.Config.Display.PaperWhiteNits,
                DisplaySettings.PaperWhiteMin,
                DisplaySettings.PaperWhiteMax,
                DisplaySettings.DefaultPaperWhite));
            float sanitizedPeak = Mathf.Max(
                paperWhite,
                QuantizeNits(FiniteClamp(
                    peakBrightnessNits,
                    DisplaySettings.PeakBrightnessMin,
                    DisplaySettings.PeakBrightnessMax,
                    DisplaySettings.DefaultPeakBrightness)));
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
            display.PaperWhiteNits = QuantizeNits(FiniteClamp(
                display.PaperWhiteNits,
                DisplaySettings.PaperWhiteMin,
                DisplaySettings.PaperWhiteMax,
                DisplaySettings.DefaultPaperWhite));
            display.PeakBrightnessNits = Mathf.Max(
                display.PaperWhiteNits,
                QuantizeNits(FiniteClamp(
                    display.PeakBrightnessNits,
                    DisplaySettings.PeakBrightnessMin,
                    DisplaySettings.PeakBrightnessMax,
                    DisplaySettings.DefaultPeakBrightness)));
        }

        private static float FiniteClamp(
            float value,
            float minimum,
            float maximum,
            float fallback) =>
            float.IsNaN(value) || float.IsInfinity(value)
                ? fallback
                : Mathf.Clamp(value, minimum, maximum);

        // Квантование стоит здесь, а не в ползунке: ползунок не единственный
        // источник этих величин. Их же пишет автоопределение дисплея, которое
        // отдаёт сырые числа системы вроде 160.0, и калибровочный экран.
        // Кратность обязана держаться независимо от того, кто записал.
        private static float QuantizeNits(float value) =>
            Mathf.Round(value / DisplaySettings.BrightnessStepNits) *
            DisplaySettings.BrightnessStepNits;

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
