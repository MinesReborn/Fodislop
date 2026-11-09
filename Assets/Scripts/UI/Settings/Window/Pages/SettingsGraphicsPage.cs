#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using Kern.Core;
using Kern.Core.Interfaces;
using Kern.Core.Localization;
using Kern.Rendering;
using Kern.Rendering.Display;
using Kern.UI.Screens;
using UnityEngine;
using UnityEngine.UIElements;

namespace Kern.UI.Settings;

// Вкладка «Графика» — макет: режим окна, разрешение, качество, искажение сетки, отрисовка пикселей,
// ограничение FPS, вертикальная синхронизация, HDR, калибровка дисплея; раздел «Эффекты».
// HDR, который дисплей не умеет, — неактивный переключатель без пояснений (как в больших играх).
internal sealed class SettingsGraphicsPage
{
    private readonly UIDocument _doc;
    private readonly IClientConfigManager _config;
    private readonly DisplayManager _display;
    private readonly ISettingsApply _apply;
    private readonly ICollection<Action> _refreshers;
    private readonly ILocalizationService _loc;
    private readonly IPlayerStats? _stats;
    private readonly Action<bool> _setCapturing;

    public SettingsGraphicsPage(
        UIDocument doc,
        IClientConfigManager config,
        DisplayManager display,
        ISettingsApply apply,
        ICollection<Action> refreshers,
        ILocalizationService loc,
        IPlayerStats? stats,
        Action<bool> setCapturing)
    {
        _stats = stats;
        _setCapturing = setCapturing;
        _doc = doc;
        _config = config;
        _display = display;
        _apply = apply;
        _refreshers = refreshers;
        _loc = loc;
    }

    public void Build(VisualElement page)
    {
        // На телефоне окно, разрешение и синхронизацию задаёт система
        bool systemOwnsWindow = Application.isMobilePlatform;

        IReadOnlyList<(string Label, FullScreenMode Value)> modes = SettingsChoices.WindowModes();
        VisualElement windowMode = SettingsFields.Select(
            _loc.Get("settings.window.window_mode"),
            modes.Select(m => _loc.Get(m.Label)).ToList(),
            () => IndexOfMode(modes, Screen.fullScreenMode),
            index => _display.SetResolution(
                Screen.width,
                Screen.height,
                modes[index].Value,
                (int)Screen.currentResolution.refreshRateRatio.value),
            _refreshers);
        UIState.SetHidden(windowMode, systemOwnsWindow);
        page.Add(windowMode);

        List<Resolution> resolutions = CollectResolutions();
        VisualElement resolution = resolutions.Count == 0
            ? SettingsFields.Row(_loc.Get("settings.window.resolution"), new Label(_loc.Get("settings.display.no_resolutions")))
            : SettingsFields.Select(
                _loc.Get("settings.window.resolution"),
                resolutions.Select(r => $"{r.width}×{r.height}").ToList(),
                () => IndexOfResolution(resolutions),
                index =>
                {
                    Resolution r = resolutions[index];
                    if (r.width != Screen.width || r.height != Screen.height)
                    {
                        _display.SetResolution(r.width, r.height, Screen.fullScreenMode, (int)r.refreshRateRatio.value);
                    }
                },
                _refreshers);
        UIState.SetHidden(resolution, systemOwnsWindow);
        page.Add(resolution);

        GraphicsPreset[] presets = { GraphicsPreset.Classic, GraphicsPreset.Standard };
        page.Add(SettingsFields.Segment(
            _loc.Get("settings.window.quality"),
            presets.Select(p => _loc.Get(SettingSchema.LabelOf(p))).ToList(),
            () => Array.IndexOf(presets, _apply.SelectedPreset),
            index => _apply.SelectPreset(presets[index]),
            _refreshers));

        page.Add(SettingsFields.Switch(
            _loc.Get("settings.window.distortion"),
            () => _config.Config.Terrain.EnableDistortion,
            value => _apply.UpdateWorldMaterial(config => config.Terrain.EnableDistortion = value),
            _refreshers));

        page.Add(SettingsFields.Select(
            _loc.Get("settings.window.pixels"),
            SettingsChoices.PixelSampling.Select(p => _loc.Get(p.Label)).ToList(),
            () => Array.FindIndex(SettingsChoices.PixelSampling, p => p.Value == _config.Config.Display.PixelSampling),
            index => _display.SetPixelSamplingMode(SettingsChoices.PixelSampling[index].Value),
            _refreshers));

        page.Add(SettingsFields.Select(
            _loc.Get("settings.window.fps"),
            SettingsChoices.FrameRates.Select(f => f.Label.StartsWith("gateway.", StringComparison.Ordinal) ? _loc.Get(f.Label) : f.Label).ToList(),
            () => SettingsChoices.IndexOfFrameRate(_config.Config.Display.TargetFrameRate),
            index =>
            {
                _config.UpdateSection(config => config.Display, display => display.TargetFrameRate = SettingsChoices.FrameRates[index].Value);
                DisplayManager.ApplyFrameTiming(_config.Config.Display);
            },
            _refreshers));

        VisualElement vsync = SettingsFields.Switch(
            _loc.Get("settings.window.vsync"),
            () => _config.Config.Display.VSync,
            value => _display.SetVSync(value),
            _refreshers);
        UIState.SetHidden(vsync, systemOwnsWindow);
        page.Add(vsync);

        VisualElement hdr = SettingsFields.Switch(
            _loc.Get("settings.window.hdr"),
            () => HDROutput.AppliesAtNextStart ? HDROutput.Enabled : HDROutput.Active,
            _ => { },
            _refreshers,
            out Toggle hdrToggle);
        // Переключатель HDR пишет через RequestHDR: изменение требует подтверждения с откатом
        hdrToggle.RegisterValueChangedCallback(evt => HDRSwitch.Request(_doc, _display, _loc, hdrToggle, evt.newValue));
        page.Add(hdr);

        page.Add(SettingsFields.ButtonRow(
            _loc.Get("settings.window.calibration"),
            _loc.Get("settings.window.calibrate"),
            // Калибровка есть и в SDR (одна яркость), поэтому кнопка доступна всегда
            () => new DisplayCalibrationScreen(_doc, _config, _display, _loc, _stats, _setCapturing).Open(),
            out Button calibrate));
        calibrate.name = "SettingsCalibrate";

        void RefreshHDR()
        {
            hdrToggle.SetEnabled(HDROutput.CanSwitch);
            hdrToggle.SetValueWithoutNotify(HDROutput.AppliesAtNextStart ? HDROutput.Enabled : HDROutput.Active);
        }

        _refreshers.Add(RefreshHDR);
        hdr.schedule.Execute(RefreshHDR).Every(250);
        RefreshHDR();

        page.Add(SettingsFields.Section(_loc.Get("settings.window.effects")));
        page.Add(EffectSwitch(nameof(EffectSettings.BloomEnabled), () => _config.Config.Effects.BloomEnabled, (c, v) => c.Effects.BloomEnabled = v));
        page.Add(EffectSwitch(nameof(EffectSettings.VignetteEnabled), () => _config.Config.Effects.VignetteEnabled, (c, v) => c.Effects.VignetteEnabled = v));
        VisualElement last = EffectSwitch(nameof(EffectSettings.EigengrauEnabled), () => _config.Config.Effects.EigengrauEnabled, (c, v) => c.Effects.EigengrauEnabled = v);
        last.AddToClassList("field--last");
        page.Add(last);
    }

    private VisualElement EffectSwitch(string field, Func<bool> read, Action<ClientConfig, bool> write) =>
        SettingsFields.Switch(
            _loc.Get(SettingSchema.LabelOf<EffectSettings>(field)),
            read,
            value => _apply.UpdatePostProcess(config => write(config, value)),
            _refreshers);

    private static int IndexOfMode(IReadOnlyList<(string Label, FullScreenMode Value)> modes, FullScreenMode current)
    {
        for (int i = 0; i < modes.Count; i++)
        {
            if (modes[i].Value == current)
            {
                return i;
            }
        }

        // Режим, которого нет в списке платформы, ближе всего к «полному экрану в окне»
        for (int i = 0; i < modes.Count; i++)
        {
            if (modes[i].Value == FullScreenMode.FullScreenWindow)
            {
                return i;
            }
        }

        return 0;
    }

    // Уникальные разрешения с наибольшей частотой; текущее добавляется, если система его не перечислила
    private static List<Resolution> CollectResolutions()
    {
        var map = new Dictionary<(int, int), Resolution>();
        foreach (Resolution r in Screen.resolutions)
        {
            if (!map.TryGetValue((r.width, r.height), out Resolution existing) ||
                r.refreshRateRatio.value > existing.refreshRateRatio.value)
            {
                map[(r.width, r.height)] = r;
            }
        }

        if (Screen.width > 0 && Screen.height > 0 && !map.ContainsKey((Screen.width, Screen.height)))
        {
            map[(Screen.width, Screen.height)] = new Resolution
            {
                width = Screen.width,
                height = Screen.height,
                refreshRateRatio = Screen.currentResolution.refreshRateRatio,
            };
        }

        return map.Values.OrderBy(r => r.width).ThenBy(r => r.height).ToList();
    }

    private int IndexOfResolution(List<Resolution> resolutions)
    {
        int width = _config.Config.Display.ResolutionWidth > 0 ? _config.Config.Display.ResolutionWidth : Screen.width;
        int height = _config.Config.Display.ResolutionHeight > 0 ? _config.Config.Display.ResolutionHeight : Screen.height;
        int index = resolutions.FindIndex(r => r.width == width && r.height == height);
        return index >= 0 ? index : resolutions.FindIndex(r => r.width == Screen.width && r.height == Screen.height);
    }
}
