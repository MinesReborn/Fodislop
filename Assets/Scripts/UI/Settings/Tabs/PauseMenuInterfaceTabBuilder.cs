#nullable enable

using System;
using System.Collections.Generic;
using Kern.Core;
using Kern.Core.Interfaces;
using Kern.Core.Localization;
using UnityEngine;
using UnityEngine.UIElements;

namespace Kern.UI;

internal sealed class PauseMenuInterfaceTabBuilder
{
    private readonly UIDocument _doc;
    private readonly IClientConfigManager _clientConfig;
    private readonly ICollection<Action> _refreshers;
    private readonly ILocalizationService _loc;

    public PauseMenuInterfaceTabBuilder(
        UIDocument doc,
        IClientConfigManager clientConfig,
        ICollection<Action> refreshers,
        ILocalizationService loc)
    {
        _doc = doc;
        _clientConfig = clientConfig;
        _refreshers = refreshers;
        _loc = loc;
    }

    public VisualElement Build(ScrollView interfaceScroll)
    {
        VisualElement interfaceSection = interfaceScroll.Q<VisualElement>("InterfaceSection") ??
            throw new InvalidOperationException("[PauseMenu] InterfaceSection is missing from PauseMenu.uxml.");

        interfaceSection.Add(PauseMenuUIFactory.CreateBoundSlider<InterfaceSettings>(
            nameof(InterfaceSettings.UIScale),
            _loc,
            () => _clientConfig.Config.Interface.UIScale,
            v =>
            {
                _clientConfig.UpdateSection(config => config.Interface, settings => settings.UIScale = v);

                // The panel scale is what actually resizes the live UI;
                // saving alone would only take effect on the next launch.
                if (_doc != null && _doc.panelSettings != null)
                {
                    _doc.panelSettings.scale = v;
                }
            },
            _refreshers));

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
            _clientConfig.UpdateSection(config => config.Interface, settings => settings.ControlScheme = controlSchemeDropdown.index);
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
