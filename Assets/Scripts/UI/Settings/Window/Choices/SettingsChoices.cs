#nullable enable

using System;
using System.Collections.Generic;
using Kern.Core;
using UnityEngine;

namespace Kern.UI.Settings;

// Варианты списков окна настроек и онбординга — макет visual/kern-ui (#settingsDialog, онбординг):
// одна таблица на оба места, чтобы список FPS или масштаба не расходился между ними.
internal static class SettingsChoices
{
    // «Ограничение FPS»: -1 — без ограничения
    public static readonly (string Label, int Value)[] FrameRates =
    {
        ("gateway.onb.fps.unlimited", -1),
        ("144", 144),
        ("60", 60),
        ("30", 30),
    };

    public static readonly (string Label, float Value)[] UIScales =
    {
        ("gateway.onb.ui_scale.100", 1.00f),
        ("gateway.onb.ui_scale.115", 1.15f),
        ("gateway.onb.ui_scale.130", 1.30f),
        ("gateway.onb.ui_scale.150", 1.50f),
        ("gateway.onb.ui_scale.175", 1.75f),
    };

    public static readonly (string Label, float Value)[] TextScales =
    {
        ("gateway.onb.text_scale.small", 0.85f),
        ("gateway.onb.text_scale.normal", 1.00f),
        ("gateway.onb.text_scale.large", 1.15f),
        ("gateway.onb.text_scale.huge", 1.30f),
    };

    public static readonly (string Code, string Label)[] Languages =
    {
        ("ru", "settings.interface.language.ru"),
        ("en", "settings.interface.language.en"),
        ("zh", "settings.interface.language.zh"),
        ("zh-hant", "settings.interface.language.zh_hant"),
    };

    // «Режим для дальтоников»
    public static readonly (string Label, ColorVisionMode Value)[] ColorVision =
    {
        ("settings.window.color_vision_off", ColorVisionMode.Off),
        ("settings.window.color_vision_protanopia", ColorVisionMode.Protanopia),
        ("settings.window.color_vision_deuteranopia", ColorVisionMode.Deuteranopia),
        ("settings.window.color_vision_tritanopia", ColorVisionMode.Tritanopia),
    };

    public static readonly (string Label, PixelSamplingMode Value)[] PixelSampling =
    {
        ("settings.window.pixels_smooth", PixelSamplingMode.SmoothFiltered),
        ("settings.window.pixels_perfect", PixelSamplingMode.PixelPerfect),
        ("settings.window.pixels_raw", PixelSamplingMode.Raw),
    };

    // Режим окна — режимы Unity, которые есть на платформе: эксклюзивный полный экран есть только
    // на Windows, развёрнутое окно — только на macOS
    public static IReadOnlyList<(string Label, FullScreenMode Value)> WindowModes()
    {
        var modes = new List<(string, FullScreenMode)>();
#if UNITY_STANDALONE_WIN
        modes.Add(("settings.window.mode_exclusive", FullScreenMode.ExclusiveFullScreen));
#endif
        modes.Add(("settings.window.mode_fullscreen_window", FullScreenMode.FullScreenWindow));
#if UNITY_STANDALONE_OSX
        modes.Add(("settings.window.mode_maximized", FullScreenMode.MaximizedWindow));
#endif
        modes.Add(("settings.window.mode_windowed", FullScreenMode.Windowed));
        return modes;
    }

    // Ближайшая ступень к сохранённому значению
    public static int NearestIndex<T>(IReadOnlyList<(string Label, T Value)> table, Func<T, float> toFloat, float value)
    {
        int best = 0;
        for (int i = 1; i < table.Count; i++)
        {
            if (Math.Abs(toFloat(table[i].Value) - value) < Math.Abs(toFloat(table[best].Value) - value))
            {
                best = i;
            }
        }

        return best;
    }

    public static int IndexOfFrameRate(int value)
    {
        for (int i = 0; i < FrameRates.Length; i++)
        {
            if (FrameRates[i].Value == value)
            {
                return i;
            }
        }

        return value <= 0 ? 0 : NearestIndex(FrameRates, v => v <= 0 ? float.MaxValue : v, value);
    }

    public static int IndexOfLanguage(string code)
    {
        for (int i = 0; i < Languages.Length; i++)
        {
            if (Languages[i].Code == code)
            {
                return i;
            }
        }

        return 0;
    }
}
