#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using Kern.Core;
using Kern.Core.Interfaces;
using Kern.Core.Localization;
using Kern.UI.Screens;
using UnityEngine.UIElements;

namespace Kern.UI.Settings;

// Вкладка «Интерфейс» — макет: язык, масштаб интерфейса, размер текста, режим для дальтоников.
// Язык применяется сразу: экраны подписаны на смену языка и пересобирают тексты.
internal sealed class SettingsInterfacePage
{
    private readonly UIDocument _doc;
    private readonly IClientConfigManager _config;
    private readonly ISettingsApply _apply;
    private readonly ICollection<Action> _refreshers;
    private readonly ILocalizationService _loc;

    public SettingsInterfacePage(
        UIDocument doc,
        IClientConfigManager config,
        ISettingsApply apply,
        ICollection<Action> refreshers,
        ILocalizationService loc)
    {
        _doc = doc;
        _config = config;
        _apply = apply;
        _refreshers = refreshers;
        _loc = loc;
    }

    public void Build(VisualElement page)
    {
        page.Add(SettingsFields.Select(
            _loc.Get("settings.window.language"),
            SettingsChoices.Languages.Select(l => _loc.Get(l.Label)).ToList(),
            () => SettingsChoices.IndexOfLanguage(_loc.CurrentLanguage),
            index =>
            {
                string code = SettingsChoices.Languages[index].Code;
                if (code != _loc.CurrentLanguage)
                {
                    _loc.SetLanguage(code);
                }
            },
            _refreshers));

        page.Add(SettingsFields.Select(
            _loc.Get("settings.window.ui_scale"),
            SettingsChoices.UIScales.Select(s => _loc.Get(s.Label)).ToList(),
            () => SettingsChoices.NearestIndex(SettingsChoices.UIScales, v => v, _config.Config.Interface.UIScale),
            index =>
            {
                float scale = SettingsChoices.UIScales[index].Value;
                _config.UpdateSection(config => config.Interface, settings => settings.UIScale = scale);
                // Масштаб панели меняет живой интерфейс сразу, а не со следующего запуска
                UIScreen.SetUserScale(_doc, scale);
            },
            _refreshers));

        page.Add(SettingsFields.Select(
            _loc.Get("settings.window.text_size"),
            SettingsChoices.TextScales.Select(s => _loc.Get(s.Label)).ToList(),
            () => SettingsChoices.NearestIndex(SettingsChoices.TextScales, v => v, _config.Config.Interface.TextScale),
            index =>
            {
                float scale = SettingsChoices.TextScales[index].Value;
                _config.UpdateSection(config => config.Interface, settings => settings.TextScale = scale);
                UIScreen.SetTextScale(scale);
            },
            _refreshers));

        VisualElement colorVision = SettingsFields.Select(
            _loc.Get("settings.window.color_vision"),
            SettingsChoices.ColorVision.Select(c => _loc.Get(c.Label)).ToList(),
            () => Array.FindIndex(SettingsChoices.ColorVision, c => c.Value == _config.Config.Effects.ColorVision),
            index =>
            {
                ColorVisionMode mode = SettingsChoices.ColorVision[index].Value;
                _apply.UpdatePostProcess(config => config.Effects.ColorVision = mode);
            },
            _refreshers);
        colorVision.AddToClassList("field--last");
        page.Add(colorVision);
    }
}
