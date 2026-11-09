#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using Kern.Core;
using Kern.Core.Interfaces;
using Kern.Core.Localization;
using UnityEngine;
using UnityEngine.UIElements;

using Kern.Rendering.Display;
using Kern.UI.Screens;

namespace Kern.UI.Gateway;

public sealed class GatewayOnboarding
{
    private const string StepActiveClass = "onb-step--active";
    private const string PillActiveClass = "onb-pill--active";
    private const string PillDoneClass = "onb-pill--done";

    private static readonly string[] s_stepTitles =
    {
        "gateway.onb.step1_title",
        "gateway.onb.step2_title",
        "gateway.onb.step3_title",
    };

    // Ступени качества — те же, что в паузе: «Классика» без освещения, «Стандарт» с ним.
    private static readonly Kern.Rendering.GraphicsPreset[] s_graphicsPresets =
    {
        Kern.Rendering.GraphicsPreset.Classic,
        Kern.Rendering.GraphicsPreset.Standard,
    };

    // Списки — общие с окном настроек (SettingsChoices), чтобы не расходились
    private static readonly (string Label, int Value)[] s_frameRates = Kern.UI.Settings.SettingsChoices.FrameRates;
    private static readonly (string Label, float Value)[] s_uiScales = Kern.UI.Settings.SettingsChoices.UIScales;
    private static readonly (string Code, string Label)[] s_languages = Kern.UI.Settings.SettingsChoices.Languages;
    private static readonly (string Label, float Value)[] s_textScales = Kern.UI.Settings.SettingsChoices.TextScales;

    private readonly VisualElement _root;
    private readonly VisualElement _overlay;
    private readonly IClientConfigManager _clientConfig;
    private readonly ILocalizationService _loc;
    private readonly Action _onFinished;
    private readonly Action<float> _onApplyUIScale;
    private readonly UIDocument _document;
    private readonly Kern.Rendering.Display.DisplayManager _display;
    private readonly IPlayerStats _stats;
    private int _step;

    private GatewayOnboarding(
        VisualElement root,
        VisualElement overlay,
        IClientConfigManager clientConfig,
        ILocalizationService loc,
        Action onFinished,
        Action<float> onApplyUIScale,
        UIDocument document,
        Kern.Rendering.Display.DisplayManager display,
        IPlayerStats stats)
    {
        _document = document;
        _display = display;
        _stats = stats;
        _root = root;
        _overlay = overlay;
        _clientConfig = clientConfig;
        _loc = loc;
        _onFinished = onFinished;
        _onApplyUIScale = onApplyUIScale;

        BindControls();
    }

    public static GatewayOnboarding? TryCreate(
        VisualElement root,
        IClientConfigManager clientConfig,
        ILocalizationService loc,
        Action onFinished,
        Action<float> onApplyUIScale,
        UIDocument document,
        Kern.Rendering.Display.DisplayManager display,
        IPlayerStats stats)
    {
        VisualElement? overlay = root.Q<VisualElement>("OnboardingOverlay");
        if (overlay == null)
        {
            return null;
        }

        return new GatewayOnboarding(root, overlay, clientConfig, loc, onFinished, onApplyUIScale, document, display, stats);
    }

    public void Show()
    {
        ApplyStep(0);
    }

    public void ApplyLocalizedText()
    {
        ApplyStep(_step);

        var uiScale = _root.Q<DropdownField>("OnbUIScale");
        if (uiScale != null)
        {
            uiScale.choices = new List<string>();
            foreach ((string label, float _) in s_uiScales)
            {
                uiScale.choices.Add(_loc.Get(label));
            }
        }

        var frameRate = _root.Q<DropdownField>("OnbFrameRate");
        if (frameRate != null)
        {
            frameRate.choices = new List<string>();
            foreach ((string label, int _) in s_frameRates)
            {
                frameRate.choices.Add(label.StartsWith("gateway.") ? _loc.Get(label) : label);
            }
        }

        SetChoices(_root.Q<DropdownField>("OnbLanguage"), s_languages.Select(l => _loc.Get(l.Label)));
        SetChoices(_root.Q<DropdownField>("OnbTextScale"), s_textScales.Select(t => _loc.Get(t.Label)));
        SetChoices(_root.Q<DropdownField>("OnbColorVision"), Kern.UI.Settings.SettingsChoices.ColorVision.Select(c => _loc.Get(c.Label)));
    }

    // Подписи пересобираются на смене языка, выбранный пункт остаётся
    private static void SetChoices(DropdownField? field, IEnumerable<string> labels)
    {
        if (field == null)
        {
            return;
        }

        int index = field.index;
        field.choices = labels.ToList();
        field.SetValueWithoutNotify(index >= 0 && index < field.choices.Count ? field.choices[index] : field.value);
    }

    private void BindControls()
    {
        ClientConfig config = _clientConfig.Config;

        var uiScale = _root.Q<DropdownField>("OnbUIScale");
        if (uiScale != null)
        {
            var labels = new List<string>();
            foreach ((string label, float _) in s_uiScales)
            {
                labels.Add(_loc.Get(label));
            }

            uiScale.choices = labels;
            uiScale.index = IndexOfUIScale(config.Interface.UIScale);
            uiScale.RegisterValueChangedCallback(_ => _onApplyUIScale(ValueOfUIScale(uiScale.index)));
        }

        var frameRate = _root.Q<DropdownField>("OnbFrameRate");
        if (frameRate != null)
        {
            var labels = new List<string>();
            foreach ((string label, int _) in s_frameRates)
            {
                labels.Add(label.StartsWith("gateway.") ? _loc.Get(label) : label);
            }

            frameRate.choices = labels;
            frameRate.index = IndexOfFrameRate(config.Display.TargetFrameRate);
        }

        var preset = _root.Q<DropdownField>("OnbGraphicsPreset");
        if (preset != null)
        {
            var labels = new List<string>();
            foreach (Kern.Rendering.GraphicsPreset value in s_graphicsPresets)
            {
                labels.Add(_loc.Get(SettingSchema.LabelOf(value)));
            }

            preset.choices = labels;
            preset.index = Array.IndexOf(s_graphicsPresets, _clientConfig.SelectedGraphicsPreset);
        }

        var vsync = _root.Q<Toggle>("OnbVSync");
        if (vsync != null)
        {
            vsync.SetValueWithoutNotify(config.Display.VSync);
        }

        // Язык применяется сразу: экраны пересобирают текст по OnLanguageChanged
        var language = _root.Q<DropdownField>("OnbLanguage");
        if (language != null)
        {
            language.choices = s_languages.Select(l => _loc.Get(l.Label)).ToList();
            language.index = Math.Max(0, Array.FindIndex(s_languages, l => l.Code == _loc.CurrentLanguage));
            language.RegisterValueChangedCallback(_ =>
            {
                string code = s_languages[Mathf.Clamp(language.index, 0, s_languages.Length - 1)].Code;
                if (code != _loc.CurrentLanguage)
                {
                    _loc.SetLanguage(code);
                }
            });
        }

        // Размер текста виден сразу, сохраняется по «Начать» вместе с остальным
        var textScale = _root.Q<DropdownField>("OnbTextScale");
        if (textScale != null)
        {
            textScale.choices = s_textScales.Select(t => _loc.Get(t.Label)).ToList();
            textScale.index = IndexOfTextScale(config.Interface.TextScale);
            textScale.RegisterValueChangedCallback(_ => UIScreen.SetTextScale(ValueOfTextScale(textScale.index)));
        }

        var colorVision = _root.Q<DropdownField>("OnbColorVision");
        if (colorVision != null)
        {
            colorVision.choices = Kern.UI.Settings.SettingsChoices.ColorVision.Select(c => _loc.Get(c.Label)).ToList();
            colorVision.index = Math.Max(0, Array.FindIndex(Kern.UI.Settings.SettingsChoices.ColorVision, c => c.Value == config.Effects.ColorVision));
        }

        // HDR, который дисплей не умеет, — неактивный переключатель; смена режима — с подтверждением и откатом
        var hdr = _root.Q<Toggle>("OnbHdr");
        if (hdr != null)
        {
            hdr.SetEnabled(Kern.Rendering.Display.HDROutput.CanSwitch);
            hdr.SetValueWithoutNotify(Kern.Rendering.Display.HDROutput.AppliesAtNextStart
                ? Kern.Rendering.Display.HDROutput.Enabled
                : Kern.Rendering.Display.HDROutput.Active);
            hdr.RegisterValueChangedCallback(evt => Kern.UI.Settings.HDRSwitch.Request(_document, _display, _loc, hdr, evt.newValue));
        }

        // Калибровка — тот же экран, что в настройках: в HDR три шага, в SDR — яркость
        var calibrate = _root.Q<Button>("OnbCalibrate");
        if (calibrate != null)
        {
            calibrate.clicked += () => new Kern.UI.Settings.DisplayCalibrationScreen(_document, _clientConfig, _display, _loc, _stats, _ => { }).Open();
        }

        var masterVol = _root.Q<Slider>("OnbMasterVolume");
        var masterVolLbl = _root.Q<Label>("OnbMasterVolumeLabel");
        if (masterVol != null)
        {
            Kern.UI.Controls.KernRange.Decorate(masterVol);
            masterVol.value = Mathf.RoundToInt(config.Audio.MasterVolume * 100f);
            if (masterVolLbl != null)
            {
                masterVolLbl.text = $"{Mathf.RoundToInt(masterVol.value)}%";
            }

            masterVol.RegisterValueChangedCallback(evt =>
            {
                if (masterVolLbl != null)
                {
                    masterVolLbl.text = $"{Mathf.RoundToInt(evt.newValue)}%";
                }
            });
        }

        // «Звук в фоне» — обратное MuteInBackground
        var backgroundSound = _root.Q<Toggle>("OnbBackgroundSound");
        if (backgroundSound != null)
        {
            backgroundSound.SetValueWithoutNotify(!config.Audio.MuteInBackground);
        }

        var prev = _root.Q<Button>("OnbPrevButton");
        if (prev != null)
        {
            prev.clicked += () => ApplyStep(_step - 1);
        }

        var next = _root.Q<Button>("OnbNextButton");
        if (next != null)
        {
            next.clicked += OnNext;
        }

        var skip = _root.Q<Button>("OnbSkipButton");
        if (skip != null)
        {
            skip.clicked += FinishOnboarding;
        }
    }

    private void OnNext()
    {
        if (_step >= s_stepTitles.Length - 1)
        {
            FinishOnboarding();
            return;
        }

        ApplyStep(_step + 1);
    }

    private void ApplyStep(int step)
    {
        _step = Mathf.Clamp(step, 0, s_stepTitles.Length - 1);

        for (int i = 0; i < s_stepTitles.Length; i++)
        {
            var content = _root.Q<VisualElement>($"OnbStep{i + 1}");
            content?.EnableInClassList(StepActiveClass, i == _step);

            var pill = _root.Q<Label>($"OnbPill{i + 1}");
            if (pill == null)
            {
                continue;
            }

            // Номер шага — как в макете: «01 · Доступность»
            pill.text = $"{i + 1:00} · {_loc.Get($"gateway.onb.pill{i + 1}")}";
            pill.EnableInClassList(PillActiveClass, i == _step);
            pill.EnableInClassList(PillDoneClass, i < _step);
        }

        var title = _root.Q<Label>("OnboardingTitle");
        if (title != null)
        {
            title.text = _loc.Get(s_stepTitles[_step]);
        }

        // На первом шаге «Назад» недоступна, а не спрятана: подвал не прыгает (макет)
        _root.Q<Button>("OnbPrevButton")?.SetEnabled(_step != 0);

        var next = _root.Q<Button>("OnbNextButton");
        if (next != null)
        {
            next.text = _step >= s_stepTitles.Length - 1
                ? _loc.Get("gateway.onb.start")
                : _loc.Get("gateway.onb.next");
        }
    }

    private void FinishOnboarding()
    {
        SaveSettings();
        _onFinished();
    }

    private void SaveSettings()
    {
        var preset = _root.Q<DropdownField>("OnbGraphicsPreset");
        if (preset != null && preset.index >= 0 && preset.index < s_graphicsPresets.Length)
        {
            _clientConfig.SelectGraphicsPreset(s_graphicsPresets[preset.index]);
        }

        _clientConfig.UpdateAndSave(config =>
        {
            var uiScale = _root.Q<DropdownField>("OnbUIScale");
            if (uiScale != null)
            {
                config.Interface.UIScale = ValueOfUIScale(uiScale.index);
            }

            var frameRate = _root.Q<DropdownField>("OnbFrameRate");
            if (frameRate != null && frameRate.index >= 0 && frameRate.index < s_frameRates.Length)
            {
                config.Display.TargetFrameRate = s_frameRates[frameRate.index].Value;
            }

            var vsync = _root.Q<Toggle>("OnbVSync");
            if (vsync != null)
            {
                config.Display.VSync = vsync.value;
            }

            var textScale = _root.Q<DropdownField>("OnbTextScale");
            if (textScale != null)
            {
                config.Interface.TextScale = ValueOfTextScale(textScale.index);
            }

            var masterVol = _root.Q<Slider>("OnbMasterVolume");
            if (masterVol != null)
            {
                config.Audio.MasterVolume = masterVol.value / 100f;
            }

            var backgroundSound = _root.Q<Toggle>("OnbBackgroundSound");
            if (backgroundSound != null)
            {
                config.Audio.MuteInBackground = !backgroundSound.value;
            }

            var colorVision = _root.Q<DropdownField>("OnbColorVision");
            if (colorVision != null && colorVision.index >= 0 && colorVision.index < Kern.UI.Settings.SettingsChoices.ColorVision.Length)
            {
                config.Effects.ColorVision = Kern.UI.Settings.SettingsChoices.ColorVision[colorVision.index].Value;
            }
        });

        Kern.Rendering.Display.DisplayManager.ApplyFrameTiming(_clientConfig.Config.Display);
    }

    private static float ValueOfTextScale(int index)
    {
        return index >= 0 && index < s_textScales.Length ? s_textScales[index].Value : 1f;
    }

    // Ближайшая ступень: в настройках множитель ставится ползунком с шагом 0.05
    private static int IndexOfTextScale(float value)
    {
        int best = 1;
        for (int i = 0; i < s_textScales.Length; i++)
        {
            if (Math.Abs(s_textScales[i].Value - value) < Math.Abs(s_textScales[best].Value - value))
            {
                best = i;
            }
        }

        return best;
    }

    private static float ValueOfUIScale(int index)
    {
        return index >= 0 && index < s_uiScales.Length ? s_uiScales[index].Value : 1f;
    }

    private static int IndexOfUIScale(float value)
    {
        int bestIndex = 0;
        float minDiff = float.MaxValue;
        for (int i = 0; i < s_uiScales.Length; i++)
        {
            float diff = Mathf.Abs(s_uiScales[i].Value - value);
            if (diff < minDiff)
            {
                minDiff = diff;
                bestIndex = i;
            }
        }

        return bestIndex;
    }

    private static int IndexOfFrameRate(int value)
    {
        for (int i = 0; i < s_frameRates.Length; i++)
        {
            if (s_frameRates[i].Value == value)
            {
                return i;
            }
        }

        return 0;
    }
}
