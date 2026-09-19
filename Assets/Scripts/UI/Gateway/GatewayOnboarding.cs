#nullable enable

using System;
using System.Collections.Generic;
using Kern.Core;
using Kern.Core.Interfaces;
using Kern.Core.Localization;
using UnityEngine;
using UnityEngine.UIElements;

namespace Kern.UI;

public sealed class GatewayOnboarding
{
    private const string StepActiveClass = "onb-step--active";
    private const string PillActiveClass = "onb-pill--active";
    private const string PillDoneClass = "onb-pill--done";
    private const string ButtonHiddenClass = "onb-btn--hidden";

    private static readonly string[] _StepTitles =
    {
        "gateway.onb.step1_title",
        "gateway.onb.step2_title",
        "gateway.onb.step3_title",
    };

    private static readonly (string Label, int Value)[] _FrameRates =
    {
        ("gateway.onb.fps.unlimited", -1),
        ("144 FPS", 144),
        ("120 FPS", 120),
        ("60 FPS", 60),
    };

    private static readonly (string Label, float Value)[] _UIScales =
    {
        ("gateway.onb.ui_scale.100", 1.00f),
        ("gateway.onb.ui_scale.115", 1.15f),
        ("gateway.onb.ui_scale.130", 1.30f),
        ("gateway.onb.ui_scale.150", 1.50f),
        ("gateway.onb.ui_scale.175", 1.75f),
    };

    private readonly VisualElement _root;
    private readonly VisualElement _overlay;
    private readonly IClientConfigManager _clientConfig;
    private readonly ILocalizationService _loc;
    private readonly Action _onFinished;
    private readonly Action<float> _onApplyUIScale;
    private int _step;

    private GatewayOnboarding(
        VisualElement root,
        VisualElement overlay,
        IClientConfigManager clientConfig,
        ILocalizationService loc,
        Action onFinished,
        Action<float> onApplyUIScale)
    {
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
        Action<float> onApplyUIScale)
    {
        VisualElement? overlay = root.Q<VisualElement>("OnboardingOverlay");
        if (overlay == null)
        {
            return null;
        }

        return new GatewayOnboarding(root, overlay, clientConfig, loc, onFinished, onApplyUIScale);
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
            foreach ((string label, float _) in _UIScales)
            {
                uiScale.choices.Add(_loc.Get(label));
            }
        }

        var frameRate = _root.Q<DropdownField>("OnbFrameRate");
        if (frameRate != null)
        {
            frameRate.choices = new List<string>();
            foreach ((string label, int _) in _FrameRates)
            {
                frameRate.choices.Add(label.StartsWith("gateway.") ? _loc.Get(label) : label);
            }
        }

        var colorblind = _root.Q<DropdownField>("OnbColorblind");
        if (colorblind != null)
        {
            colorblind.choices = new List<string>
            {
                _loc.Get("gateway.onb.colorblind.none"),
                _loc.Get("gateway.onb.colorblind.deuteranopia"),
                _loc.Get("gateway.onb.colorblind.protanopia"),
                _loc.Get("gateway.onb.colorblind.tritanopia"),
                _loc.Get("gateway.onb.colorblind.high_contrast"),
            };
        }

        var photoSens = _root.Q<DropdownField>("OnbPhotosensitivity");
        if (photoSens != null)
        {
            photoSens.choices = new List<string>
            {
                _loc.Get("gateway.onb.photosens.off"),
                _loc.Get("gateway.onb.photosens.on"),
            };
        }

        var controlScheme = _root.Q<DropdownField>("OnbControlScheme");
        if (controlScheme != null)
        {
            controlScheme.choices = new List<string>
            {
                _loc.Get("gateway.onb.controls.keyboard"),
                _loc.Get("gateway.onb.controls.mouse"),
            };
        }
    }

    private void BindControls()
    {
        ClientConfig config = _clientConfig.Config;

        var uiScale = _root.Q<DropdownField>("OnbUIScale");
        if (uiScale != null)
        {
            var labels = new List<string>();
            foreach ((string label, float _) in _UIScales)
            {
                labels.Add(_loc.Get(label));
            }

            uiScale.choices = labels;
            uiScale.index = IndexOfUIScale(config.Interface.UIScale);
            uiScale.RegisterValueChangedCallback(_ => _onApplyUIScale(ValueOfUIScale(uiScale.index)));
        }

        var colorblind = _root.Q<DropdownField>("OnbColorblind");
        if (colorblind != null)
        {
            colorblind.choices = new List<string>
            {
                _loc.Get("gateway.onb.colorblind.none"),
                _loc.Get("gateway.onb.colorblind.deuteranopia"),
                _loc.Get("gateway.onb.colorblind.protanopia"),
                _loc.Get("gateway.onb.colorblind.tritanopia"),
                _loc.Get("gateway.onb.colorblind.high_contrast"),
            };
            colorblind.index = Mathf.Clamp(config.Accessibility.ColorblindMode, 0, 4);
        }

        var photoSens = _root.Q<DropdownField>("OnbPhotosensitivity");
        if (photoSens != null)
        {
            photoSens.choices = new List<string>
            {
                _loc.Get("gateway.onb.photosens.off"),
                _loc.Get("gateway.onb.photosens.on"),
            };
            photoSens.index = config.Accessibility.ReducePhotosensitivity ? 1 : 0;
        }

        var frameRate = _root.Q<DropdownField>("OnbFrameRate");
        if (frameRate != null)
        {
            var labels = new List<string>();
            foreach ((string label, int _) in _FrameRates)
            {
                labels.Add(label.StartsWith("gateway.") ? _loc.Get(label) : label);
            }

            frameRate.choices = labels;
            frameRate.index = IndexOfFrameRate(config.Display.TargetFrameRate);
        }

        var preset = _root.Q<DropdownField>("OnbGraphicsPreset");
        if (preset != null)
        {
            preset.choices = new List<string>
            {
                _loc.Get("gateway.onb.preset.ultra"),
                _loc.Get("gateway.onb.preset.high"),
                _loc.Get("gateway.onb.preset.medium"),
                _loc.Get("gateway.onb.preset.fast"),
            };
            preset.index = 0;
        }

        var vsync = _root.Q<Toggle>("OnbVSync");
        if (vsync != null)
        {
            vsync.SetValueWithoutNotify(config.Display.VSync);
        }

        var controlScheme = _root.Q<DropdownField>("OnbControlScheme");
        if (controlScheme != null)
        {
            controlScheme.choices = new List<string>
            {
                _loc.Get("gateway.onb.controls.keyboard"),
                _loc.Get("gateway.onb.controls.mouse"),
            };
            controlScheme.index = Mathf.Clamp(config.Interface.ControlScheme, 0, 1);
        }

        var masterVol = _root.Q<Slider>("OnbMasterVolume");
        var masterVolLbl = _root.Q<Label>("OnbMasterVolumeLabel");
        if (masterVol != null)
        {
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

        var mute = _root.Q<Toggle>("OnbMuteInBackground");
        if (mute != null)
        {
            mute.SetValueWithoutNotify(config.Audio.MuteInBackground);
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
        if (_step >= _StepTitles.Length - 1)
        {
            FinishOnboarding();
            return;
        }

        ApplyStep(_step + 1);
    }

    private void ApplyStep(int step)
    {
        _step = Mathf.Clamp(step, 0, _StepTitles.Length - 1);

        for (int i = 0; i < _StepTitles.Length; i++)
        {
            var content = _root.Q<VisualElement>($"OnbStep{i + 1}");
            content?.EnableInClassList(StepActiveClass, i == _step);

            var pill = _root.Q<Label>($"OnbPill{i + 1}");
            if (pill == null)
            {
                continue;
            }

            pill.EnableInClassList(PillActiveClass, i == _step);
            pill.EnableInClassList(PillDoneClass, i < _step);
        }

        var title = _root.Q<Label>("OnboardingTitle");
        if (title != null)
        {
            title.text = _loc.Get(_StepTitles[_step]);
        }

        _root.Q<Button>("OnbPrevButton")?.EnableInClassList(ButtonHiddenClass, _step == 0);

        var next = _root.Q<Button>("OnbNextButton");
        if (next != null)
        {
            next.text = _step >= _StepTitles.Length - 1
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
        _clientConfig.UpdateAndSave(config =>
        {
            var uiScale = _root.Q<DropdownField>("OnbUIScale");
            if (uiScale != null)
            {
                config.Interface.UIScale = ValueOfUIScale(uiScale.index);
            }

            var colorblind = _root.Q<DropdownField>("OnbColorblind");
            if (colorblind != null && colorblind.index >= 0)
            {
                config.Accessibility.ColorblindMode = colorblind.index;
            }

            var photoSens = _root.Q<DropdownField>("OnbPhotosensitivity");
            if (photoSens != null && photoSens.index >= 0)
            {
                config.Accessibility.ReducePhotosensitivity = photoSens.index == 1;
            }

            var frameRate = _root.Q<DropdownField>("OnbFrameRate");
            if (frameRate != null && frameRate.index >= 0 && frameRate.index < _FrameRates.Length)
            {
                config.Display.TargetFrameRate = _FrameRates[frameRate.index].Value;
            }

            var vsync = _root.Q<Toggle>("OnbVSync");
            if (vsync != null)
            {
                config.Display.VSync = vsync.value;
            }

            var controlScheme = _root.Q<DropdownField>("OnbControlScheme");
            if (controlScheme != null && controlScheme.index >= 0)
            {
                config.Interface.ControlScheme = controlScheme.index;
            }

            var masterVol = _root.Q<Slider>("OnbMasterVolume");
            if (masterVol != null)
            {
                config.Audio.MasterVolume = masterVol.value / 100f;
            }

            var mute = _root.Q<Toggle>("OnbMuteInBackground");
            if (mute != null)
            {
                config.Audio.MuteInBackground = mute.value;
            }
        });

        Kern.Rendering.DisplayManager.ApplyFrameTiming(_clientConfig.Config.Display);
    }

    private static float ValueOfUIScale(int index)
    {
        return index >= 0 && index < _UIScales.Length ? _UIScales[index].Value : 1f;
    }

    private static int IndexOfUIScale(float value)
    {
        int bestIndex = 0;
        float minDiff = float.MaxValue;
        for (int i = 0; i < _UIScales.Length; i++)
        {
            float diff = Mathf.Abs(_UIScales[i].Value - value);
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
        for (int i = 0; i < _FrameRates.Length; i++)
        {
            if (_FrameRates[i].Value == value)
            {
                return i;
            }
        }

        return 0;
    }
}
