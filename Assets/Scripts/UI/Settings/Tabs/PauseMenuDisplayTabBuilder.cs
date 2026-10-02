#nullable enable

using System;
using System.Collections.Generic;
using Kern.Core;
using Kern.Core.Interfaces;
using Kern.Core.Localization;
using Kern.Rendering;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UIElements;

namespace Kern.UI;

internal sealed class PauseMenuDisplayTabBuilder
{
    // Окно подтверждения живёт в корне документа, а не внутри вкладки:
    // вкладку закрывают, а откат режима вывода обязан пережить закрытие.
    private readonly UIDocument _doc;
    private readonly IClientConfigManager _clientConfig;
    private readonly DisplayManager _displayManager;
    private readonly ICollection<Action> _refreshers;
    private readonly ILocalizationService _loc;

    private Button? _fullscreenButton;
    private Action? _refreshResolutionDropdown;

    public PauseMenuDisplayTabBuilder(
        UIDocument doc,
        IClientConfigManager clientConfig,
        DisplayManager displayManager,
        ICollection<Action> refreshers,
        ILocalizationService loc)
    {
        _doc = doc;
        _clientConfig = clientConfig;
        _displayManager = displayManager;
        _refreshers = refreshers;
        _loc = loc;
    }

    public VisualElement Build(ScrollView displayScroll)
    {
        VisualElement displaySection = displayScroll.Q<VisualElement>("DisplaySection") ??
            throw new InvalidOperationException("[PauseMenu] DisplaySection is missing from PauseMenu.uxml.");
        VisualElement HDROutputGroup =
            displayScroll.Q<VisualElement>("HDROutputGroup") ??
            throw new InvalidOperationException(
                "[PauseMenu] HDROutputGroup is missing from PauseMenu.uxml.");

        _fullscreenButton = new Button(ToggleFullscreen);
        _fullscreenButton.text = Screen.fullScreen ? _loc.Get("menu.settings.fullscreen") : _loc.Get("settings.display.windowed");
        _fullscreenButton.AddToClassList("pause-btn");
        displaySection.Add(_fullscreenButton);

        var resolutions = Screen.resolutions;
        var resolutionMap = new Dictionary<string, Resolution>();
        foreach (var res in resolutions)
        {
            var key = $"{res.width}x{res.height}";
            if (!resolutionMap.TryGetValue(key, out Resolution existing) ||
                res.refreshRateRatio.value > existing.refreshRateRatio.value)
            {
                resolutionMap[key] = res;
            }
        }

        if (Screen.width > 0 && Screen.height > 0)
        {
            var currentKey = $"{Screen.width}x{Screen.height}";
            if (!resolutionMap.ContainsKey(currentKey))
            {
                resolutionMap[currentKey] = new Resolution
                {
                    width = Screen.width,
                    height = Screen.height,
                    refreshRateRatio = Screen.currentResolution.refreshRateRatio,
                };
            }
        }

        var uniqueResolutions = new List<Resolution>(resolutionMap.Values);
        uniqueResolutions.Sort((a, b) =>
        {
            int cmp = a.width.CompareTo(b.width);
            return cmp != 0 ? cmp : a.height.CompareTo(b.height);
        });

        int FindCurrentResolutionIndex()
        {
            int targetWidth = _clientConfig.Config.Display.ResolutionWidth > 0
                ? _clientConfig.Config.Display.ResolutionWidth
                : Screen.width;
            int targetHeight = _clientConfig.Config.Display.ResolutionHeight > 0
                ? _clientConfig.Config.Display.ResolutionHeight
                : Screen.height;

            for (int i = 0; i < uniqueResolutions.Count; i++)
            {
                if (uniqueResolutions[i].width == targetWidth &&
                    uniqueResolutions[i].height == targetHeight)
                {
                    return i;
                }
            }

            for (int i = 0; i < uniqueResolutions.Count; i++)
            {
                if (uniqueResolutions[i].width == Screen.width &&
                    uniqueResolutions[i].height == Screen.height)
                {
                    return i;
                }
            }

            return -1;
        }

        var resolutionRow = new VisualElement();
        resolutionRow.AddToClassList("pause-slider-container");

        var resolutionLabel = new Label(_loc.Get("menu.settings.resolution"));
        resolutionLabel.AddToClassList("pause-slider-label");
        resolutionRow.Add(resolutionLabel);

        var resolutionDropdown = new DropdownField();
        var resolutionChoices = new List<string>(uniqueResolutions.Count);
        for (int i = 0; i < uniqueResolutions.Count; i++)
        {
            resolutionChoices.Add($"{uniqueResolutions[i].width} x {uniqueResolutions[i].height}");
        }

        if (resolutionChoices.Count == 0)
        {
            resolutionChoices.Add(_loc.Get("settings.display.no_resolutions"));
            resolutionDropdown.choices = resolutionChoices;
            resolutionDropdown.index = 0;
            resolutionDropdown.SetEnabled(false);
        }
        else
        {
            resolutionDropdown.choices = resolutionChoices;
            int initialIndex = FindCurrentResolutionIndex();
            resolutionDropdown.index = initialIndex >= 0 ? initialIndex : 0;
            resolutionDropdown.SetEnabled(true);
        }

        resolutionDropdown.RegisterValueChangedCallback(_ =>
        {
            int selectedIndex = resolutionDropdown.index;
            if (selectedIndex < 0 || selectedIndex >= uniqueResolutions.Count)
            {
                return;
            }

            Resolution resolution = uniqueResolutions[selectedIndex];
            if (resolution.width == Screen.width && resolution.height == Screen.height)
            {
                return;
            }

            _displayManager.SetResolution(
                resolution.width,
                resolution.height,
                Screen.fullScreenMode,
                (int)resolution.refreshRateRatio.value);
        });

        void RefreshResolutionDropdown()
        {
            if (uniqueResolutions.Count == 0)
            {
                return;
            }

            int idx = FindCurrentResolutionIndex();
            if (idx >= 0 && idx < resolutionDropdown.choices.Count)
            {
                resolutionDropdown.SetValueWithoutNotify(resolutionDropdown.choices[idx]);
            }
        }

        _refreshResolutionDropdown = RefreshResolutionDropdown;
        _refreshers.Add(RefreshResolutionDropdown);
        resolutionRow.Add(resolutionDropdown);
        displaySection.Add(resolutionRow);

        // Режим укладки на пиксельную сетку. Кнопкой-циклом, а не
        // выпадающим списком: вариантов три и сравнивать их надо на глаз,
        // переключая туда-сюда, — список требовал бы двух кликов на каждое
        // переключение.
        Button samplingButton = PauseMenuUIFactory.CreateBoundCycleButton(
            () => $"Pixel sampling: {_clientConfig.Config.Display.PixelSampling}",
            () =>
            {
                PixelSamplingMode next = _clientConfig.Config.Display.PixelSampling switch
                {
                    PixelSamplingMode.SmoothFiltered => PixelSamplingMode.PixelPerfect,
                    PixelSamplingMode.PixelPerfect => PixelSamplingMode.Raw,
                    _ => PixelSamplingMode.SmoothFiltered,
                };
                _displayManager.SetPixelSamplingMode(next);
            },
            _refreshers);
        samplingButton.AddToClassList("pause-btn");
        displaySection.Add(samplingButton);

        Toggle vSyncToggle = PauseMenuUIFactory.CreateBoundToggle(
            _loc.Get("menu.settings.vsync"),
            () => _clientConfig.Config.Display.VSync,
            value => _displayManager.SetVSync(value),
            _refreshers);
        displaySection.Add(vSyncToggle);
        Label syncContext = displaySection.Q<Label>("DisplaySyncContext") ??
            throw new InvalidOperationException("[PauseMenu] DisplaySyncContext is missing from PauseMenu.uxml.");
        syncContext.text = _loc.Get("settings.display.sync_editor");
        UIState.SetHidden(syncContext, !Application.isEditor);

        Toggle HDRToggle = HDROutputGroup.Q<Toggle>("HDRToggle") ??
            throw new InvalidOperationException("[PauseMenu] HDRToggle is missing from PauseMenu.uxml.");
        HDRToggle.label = _loc.Get("menu.settings.hdr");
        HDRToggle.RegisterValueChangedCallback(evt => RequestHDR(HDRToggle, evt.newValue));
        Label HDRStatus = HDROutputGroup.Q<Label>("HDRStatus") ??
            throw new InvalidOperationException("[PauseMenu] HDRStatus is missing from PauseMenu.uxml.");
        Button HDRRetry = HDROutputGroup.Q<Button>("HDRRetry") ??
            throw new InvalidOperationException("[PauseMenu] HDRRetry is missing from PauseMenu.uxml.");
        HDRRetry.text = _loc.Get("settings.display.hdr_retry");
        HDRRetry.clicked += () => HDROutput.RetryRead();

        VisualElement paperWhiteSlider = PauseMenuUIFactory.CreateBoundSlider<DisplaySettings>(
            nameof(DisplaySettings.PaperWhiteNits),
            _loc,
            () => _clientConfig.Config.Display.PaperWhiteNits,
            value => _displayManager.SetPaperWhiteNits(value),
            _refreshers,
            DisplaySettings.BrightnessStepNits);
        HDROutputGroup.Add(paperWhiteSlider);

        VisualElement peakBrightnessSlider = PauseMenuUIFactory.CreateBoundSlider<DisplaySettings>(
            nameof(DisplaySettings.PeakBrightnessNits),
            _loc,
            () => _clientConfig.Config.Display.PeakBrightnessNits,
            value => _displayManager.SetPeakBrightnessNits(value),
            _refreshers,
            DisplaySettings.BrightnessStepNits);
        HDROutputGroup.Add(peakBrightnessSlider);

        var calibrationButton = new Button(() =>
            new HDRCalibrationScreen(_doc, _clientConfig, _displayManager, _loc).Open());
        calibrationButton.text = _loc.Get("settings.display.calibration_open");
        calibrationButton.AddToClassList("pause-btn");
        HDROutputGroup.Add(calibrationButton);

        void UpdateHDRSlidersState()
        {
            bool HDROn = HDROutput.Active;
            HDRToggle.SetEnabled(HDROutput.CanSwitch);
            HDRToggle.SetValueWithoutNotify(HDROn);
            HDRStatus.text = _loc.Get(HDROutput.Status switch
            {
                HDROutputController.Phase.Pending => "settings.display.hdr_pending",
                HDROutputController.Phase.Retrying => "settings.display.hdr_retrying",
                HDROutputController.Phase.Failed => "settings.display.hdr_failed",
                HDROutputController.Phase.Unsupported => "settings.display.hdr_unsupported",
                HDROutputController.Phase.Unavailable => "settings.display.hdr_unavailable",
                HDROutputController.Phase.NotSwitchable => HDROn
                    ? "settings.display.hdr_fixed_on" : "settings.display.hdr_fixed_off",
                HDROutputController.Phase.HDR => HDROutput.RuntimeSwitchable
                    ? "settings.display.hdr_active" : "settings.display.hdr_fixed_on",
                HDROutputController.Phase.SDR => HDROutput.RuntimeSwitchable
                    ? "settings.display.hdr_inactive" : "settings.display.hdr_fixed_off",
                _ => "settings.display.hdr_pending",
            });
            HDRRetry.SetEnabled(HDROutput.Status == HDROutputController.Phase.Failed || HDROutput.CanRetryRead);
            UIState.SetHidden(HDRRetry, HDROutput.Status != HDROutputController.Phase.Failed && !HDROutput.CanRetryRead);
            paperWhiteSlider.SetEnabled(HDROn);
            peakBrightnessSlider.SetEnabled(HDROn);
            calibrationButton.SetEnabled(HDROn);
        }

        _refreshers.Add(UpdateHDRSlidersState);
        HDROutputGroup.schedule.Execute(UpdateHDRSlidersState).Every(250);
        UpdateHDRSlidersState();

        return displayScroll;
    }

    // Сколько держать окно отката. Пятнадцать секунд — столько человек
    // тратит, чтобы понять, что на экране творится что-то не то, и найти
    // мышь; меньше этого окно успевает истечь раньше, чем его прочитали.
    private const int HDRConfirmSeconds = 15;

    private void RequestHDR(Toggle toggle, bool enabled)
    {
        bool previous = !enabled;
        HDROutput.ApplyRequestResult result = _displayManager.SetHDREnabled(enabled);

        // Спрашивать есть смысл только о том, что система приняла. Отказ
        // виден по строке состояния, и окно поверх него только мешало бы.
        if (result is HDROutput.ApplyRequestResult.RejectedUnsupported
            or HDROutput.ApplyRequestResult.RejectedNotSwitchable
            or HDROutput.ApplyRequestResult.Failed)
        {
            // Метку безопасного старта ставит SetHDREnabled до запроса. Режим
            // не сменился, экран цел — снимаем, иначе следующий запуск честно
            // решит, что игру закрыли на чёрном экране, и выключит HDR.
            _displayManager.ConfirmHDRSwitchSeen();
            toggle.SetValueWithoutNotify(previous);
            return;
        }

        PauseMenuUIFactory.ShowTimedConfirmation(
            _doc,
            _loc.Get("settings.display.hdr_confirm_title"),
            _loc.Get("settings.display.hdr_confirm_desc"),
            _loc.Get("settings.display.hdr_confirm_keep"),
            _loc.Get("settings.display.hdr_confirm_revert"),
            HDRConfirmSeconds,
            () => _displayManager.ConfirmHDRSwitchSeen(),
            () =>
            {
                _displayManager.SetHDREnabled(previous);
                _displayManager.ConfirmHDRSwitchSeen();
                toggle.SetValueWithoutNotify(previous);
            });
    }

    private void ToggleFullscreen()
    {
        FullScreenMode nextMode = Screen.fullScreenMode == FullScreenMode.Windowed
            ? FullScreenMode.FullScreenWindow
            : FullScreenMode.Windowed;
        _displayManager.SetResolution(
            Screen.width,
            Screen.height,
            nextMode,
            (int)Screen.currentResolution.refreshRateRatio.value);
        if (_fullscreenButton != null)
        {
            _fullscreenButton.text = nextMode == FullScreenMode.Windowed
                ? _loc.Get("settings.display.windowed")
                : _loc.Get("menu.settings.fullscreen");
        }

        _refreshResolutionDropdown?.Invoke();
    }
}
