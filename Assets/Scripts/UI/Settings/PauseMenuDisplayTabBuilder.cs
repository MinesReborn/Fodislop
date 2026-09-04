#nullable enable

using System;
using System.Collections.Generic;
using Fodinae.Core;
using Fodinae.Core.Interfaces;
using Fodinae.Core.Localization;
using Fodinae.Rendering;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UIElements;

namespace Fodinae.UI;

/// <summary>
/// Builds the Display & Resolution Settings section in the pause menu.
/// </summary>
internal sealed class PauseMenuDisplayTabBuilder
{
    private readonly IClientConfigManager _clientConfig;
    private readonly DisplayManager _displayManager;
    private readonly ICollection<Action> _refreshers;
    private readonly ILocalizationService _loc;

    private Button? _fullscreenButton;

    public PauseMenuDisplayTabBuilder(
        IClientConfigManager clientConfig,
        DisplayManager displayManager,
        ICollection<Action> refreshers,
        ILocalizationService loc)
    {
        _clientConfig = clientConfig;
        _displayManager = displayManager;
        _refreshers = refreshers;
        _loc = loc;
    }

    public VisualElement Build(ScrollView displayScroll)
    {
        VisualElement displaySection = displayScroll.Q<VisualElement>("DisplaySection") ??
            throw new InvalidOperationException("[PauseMenu] DisplaySection is missing from PauseMenu.uxml.");
        VisualElement hdrOutputGroup =
            displayScroll.Q<VisualElement>("HDROutputGroup") ??
            throw new InvalidOperationException(
                "[PauseMenu] HDROutputGroup is missing from PauseMenu.uxml.");

        _fullscreenButton = new Button(ToggleFullscreen);
        _fullscreenButton.text = Screen.fullScreen ? _loc.Get("menu.settings.fullscreen") : _loc.Get("settings.display.windowed");
        _fullscreenButton.AddToClassList("pause-btn");
        displaySection.Add(_fullscreenButton);

        var resolutions = Screen.resolutions;
        var uniqueResolutions = new List<Resolution>();
        var seen = new HashSet<string>();
        foreach (var res in resolutions)
        {
            var key = $"{res.width}x{res.height}";
            if (seen.Add(key))
            {
                uniqueResolutions.Add(res);
            }
        }

        int currentResIndex = -1;
        for (int i = 0; i < uniqueResolutions.Count; i++)
        {
            if (uniqueResolutions[i].width == Screen.width &&
                uniqueResolutions[i].height == Screen.height)
            {
                currentResIndex = i;
                break;
            }
        }

        var resolutionButton = new Button();
        void UpdateResolutionButton()
        {
            string resolutionLabel = _loc.Get("menu.settings.resolution");
            resolutionButton.text = uniqueResolutions.Count == 0
                ? _loc.Get("settings.display.no_resolutions")
                : currentResIndex >= 0
                    ? $"{resolutionLabel}: {uniqueResolutions[currentResIndex].width} x " +
                      uniqueResolutions[currentResIndex].height
                    : $"{resolutionLabel}: {Screen.width} x {Screen.height}";
        }

        resolutionButton.clicked += () =>
        {
            if (uniqueResolutions.Count == 0)
            {
                return;
            }

            currentResIndex = (currentResIndex + 1) % uniqueResolutions.Count;
            Resolution resolution = uniqueResolutions[currentResIndex];
            _displayManager.SetResolution(
                resolution.width,
                resolution.height,
                Screen.fullScreenMode,
                (int)resolution.refreshRateRatio.value);
            UpdateResolutionButton();
        };

        resolutionButton.SetEnabled(uniqueResolutions.Count > 0);
        resolutionButton.AddToClassList("pause-btn");
        UpdateResolutionButton();
        displaySection.Add(resolutionButton);

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

        VisualElement gammaSlider = PauseMenuUIFactory.CreateBoundSlider<DisplaySettings>(
            nameof(DisplaySettings.Gamma),
            _loc,
            () => _clientConfig.Config.Display.Gamma,
            value => _displayManager.SetGamma(value),
            _refreshers);
        displaySection.Add(gammaSlider);

        Toggle hdrToggle = PauseMenuUIFactory.CreateBoundToggle(
            _loc.Get("menu.settings.hdr"),
            () => _clientConfig.Config.Display.HDREnabled,
            value => _displayManager.SetHDREnabled(value),
            _refreshers);
        hdrOutputGroup.Add(hdrToggle);

        VisualElement paperWhiteSlider = PauseMenuUIFactory.CreateBoundSlider<DisplaySettings>(
            nameof(DisplaySettings.PaperWhiteNits),
            _loc,
            () => _clientConfig.Config.Display.PaperWhiteNits,
            value => _displayManager.SetPaperWhiteNits(value),
            _refreshers);
        hdrOutputGroup.Add(paperWhiteSlider);

        VisualElement peakBrightnessSlider = PauseMenuUIFactory.CreateBoundSlider<DisplaySettings>(
            nameof(DisplaySettings.PeakBrightnessNits),
            _loc,
            () => _clientConfig.Config.Display.PeakBrightnessNits,
            value => _displayManager.SetPeakBrightnessNits(value),
            _refreshers);
        hdrOutputGroup.Add(peakBrightnessSlider);

        void UpdateHdrSlidersState()
        {
            bool hdrOn = _clientConfig.Config.Display.HDREnabled;
            gammaSlider.SetEnabled(!hdrOn);
            paperWhiteSlider.SetEnabled(hdrOn);
            peakBrightnessSlider.SetEnabled(hdrOn);
        }

        _refreshers.Add(UpdateHdrSlidersState);
        UpdateHdrSlidersState();

        return displayScroll;
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
    }
}
