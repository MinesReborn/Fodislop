#nullable enable

using System;
using System.Collections.Generic;
using Fodinae.Core;
using Fodinae.Core.Interfaces;
using Fodinae.Core.Localization;
using Fodinae.Rendering;
using UnityEngine;
using UnityEngine.UIElements;

namespace Fodinae.UI;

/// <summary>
/// Builds the Interface tab in the Pause Menu.
/// </summary>
internal sealed class PauseMenuInterfaceTabBuilder
{
    private readonly UIDocument _doc;
    private readonly IClientConfigManager _clientConfig;
    private readonly GraphicsSettingsController _graphicsSettings;
    private readonly ICollection<Action> _refreshers;
    private readonly ILocalizationService _loc;

    public PauseMenuInterfaceTabBuilder(
        UIDocument doc,
        IClientConfigManager clientConfig,
        GraphicsSettingsController graphicsSettings,
        ICollection<Action> refreshers,
        ILocalizationService loc)
    {
        _doc = doc;
        _clientConfig = clientConfig;
        _graphicsSettings = graphicsSettings;
        _refreshers = refreshers;
        _loc = loc;
    }

    public VisualElement Build(ScrollView interfaceScroll)
    {
        VisualElement interfaceSection = interfaceScroll.Q<VisualElement>("InterfaceSection") ??
            throw new InvalidOperationException("[PauseMenu] InterfaceSection is missing from PauseMenu.uxml.");

        interfaceSection.Add(PauseMenuUIFactory.CreateSlider(
            _loc.Get("menu.settings.ui_scale"),
            _clientConfig.Config.Interface.UIScale,
            v =>
            {
                _clientConfig.UpdateInterface(settings => settings.UIScale = v);

                // The panel scale is what actually resizes the live UI;
                // saving alone would only take effect on the next launch.
                if (_doc != null && _doc.panelSettings != null)
                {
                    _doc.panelSettings.scale = v;
                }
            },
            0.5f,
            2f));

        // Язык интерфейса. Применяется сразу: SetLanguage сохраняет выбор
        // в конфиг и стреляет OnLanguageChanged, на который подписаны все
        // экраны — они пересобирают свои тексты (PauseMenu пересобирает
        // дерево целиком через ApplyLocalizedText).
        var languageRow = new VisualElement();
        languageRow.AddToClassList("pause-slider-container");
        var languageLabel = new Label(_loc.Get("settings.interface.language"));
        languageLabel.AddToClassList("pause-slider-label");
        languageRow.Add(languageLabel);

        var languageDropdown = new DropdownField();
        var languageChoices = new[]
        {
            (code: "ru", _loc.Get("settings.interface.language.ru")),
            (code: "en", _loc.Get("settings.interface.language.en")),
            (code: "zh", _loc.Get("settings.interface.language.zh")),
            (code: "zh-hant", _loc.Get("settings.interface.language.zh_hant")),
        };
        languageDropdown.choices = new List<string>();
        foreach (var c in languageChoices)
        {
            languageDropdown.choices.Add(c.Item2);
        }
        languageDropdown.index = LanguageCodeToIndex(_loc.CurrentLanguage);
        languageDropdown.RegisterValueChangedCallback(_ =>
        {
            string code = languageChoices[languageDropdown.index].Item1;
            if (code != _loc.CurrentLanguage)
            {
                _loc.SetLanguage(code);
            }
        });
        // Colorblind adaptation
        var colorblindRow = new VisualElement();
        colorblindRow.AddToClassList("pause-slider-container");
        var colorblindLabel = new Label(_loc.Get("gateway.onb.colorblind_label"));
        colorblindLabel.AddToClassList("pause-slider-label");
        colorblindRow.Add(colorblindLabel);

        var colorblindDropdown = new DropdownField();
        colorblindDropdown.choices = new List<string>
        {
            _loc.Get("gateway.onb.colorblind.none"),
            _loc.Get("gateway.onb.colorblind.deuteranopia"),
            _loc.Get("gateway.onb.colorblind.protanopia"),
            _loc.Get("gateway.onb.colorblind.tritanopia"),
            _loc.Get("gateway.onb.colorblind.high_contrast"),
        };
        colorblindDropdown.index = Mathf.Clamp(_clientConfig.Config.Accessibility.ColorblindMode, 0, 4);
        colorblindDropdown.RegisterValueChangedCallback(_ =>
        {
            _graphicsSettings.UpdateAccessibilitySettings(
                settings => settings.ColorblindMode = colorblindDropdown.index);
        });
        _refreshers.Add(() =>
        {
            colorblindDropdown.index = Mathf.Clamp(_clientConfig.Config.Accessibility.ColorblindMode, 0, 4);
        });
        colorblindRow.Add(colorblindDropdown);
        interfaceSection.Add(colorblindRow);

        // Control Scheme adaptation
        var controlSchemeRow = new VisualElement();
        controlSchemeRow.AddToClassList("pause-slider-container");
        var controlSchemeLabel = new Label(_loc.Get("gateway.onb.controls_scheme_label"));
        controlSchemeLabel.AddToClassList("pause-slider-label");
        controlSchemeRow.Add(controlSchemeLabel);

        var controlSchemeDropdown = new DropdownField();
        controlSchemeDropdown.choices = new List<string>
        {
            _loc.Get("gateway.onb.controls.keyboard"),
            _loc.Get("gateway.onb.controls.mouse"),
        };
        controlSchemeDropdown.index = Mathf.Clamp(_clientConfig.Config.Interface.ControlScheme, 0, 1);
        controlSchemeDropdown.RegisterValueChangedCallback(_ =>
        {
            _clientConfig.UpdateInterface(settings => settings.ControlScheme = controlSchemeDropdown.index);
        });
        _refreshers.Add(() =>
        {
            controlSchemeDropdown.index = Mathf.Clamp(_clientConfig.Config.Interface.ControlScheme, 0, 1);
        });
        controlSchemeRow.Add(controlSchemeDropdown);            interfaceSection.Add(controlSchemeRow);

        return interfaceScroll;
    }

    private static int LanguageCodeToIndex(string code)
    {
        switch (code)
        {
            case "en":
                return 1;
            case "zh":
                return 2;
            case "zh-hant":
                return 3;
            default:
                return 0;
        }
    }

}
