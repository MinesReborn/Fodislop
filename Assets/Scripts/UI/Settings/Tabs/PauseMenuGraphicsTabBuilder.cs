#nullable enable

using System;
using System.Collections.Generic;
using Kern.Core;
using Kern.Core.Interfaces;
using Kern.Core.Localization;
using Kern.Rendering;
using UnityEngine.UIElements;

namespace Kern.UI;

internal sealed class PauseMenuGraphicsTabBuilder
{
    private readonly GraphicsSettingsController _graphicsSettings;
    private readonly IClientConfigManager _clientConfig;
    private readonly ICollection<Action> _refreshers;
    private readonly ILocalizationService _loc;
    private readonly Action _refreshAll;

    public PauseMenuGraphicsTabBuilder(
        GraphicsSettingsController graphicsSettings,
        IClientConfigManager clientConfig,
        ICollection<Action> refreshers,
        ILocalizationService loc,
        Action refreshAll)
    {
        _graphicsSettings = graphicsSettings;
        _clientConfig = clientConfig;
        _refreshers = refreshers;
        _loc = loc;
        _refreshAll = refreshAll;
    }

    public VisualElement Build(ScrollView graphicsScroll)
    {
        VisualElement graphicsSection = graphicsScroll.Q<VisualElement>("GraphicsSection") ??
            throw new InvalidOperationException("[PauseMenu] GraphicsSection is missing from PauseMenu.uxml.");

        // Ступеней две, и отличаются они только освещением, поэтому одна кнопка
        // перебирает их по кругу. Отдельного переключателя света и ручного
        // профиля больше нет: свет — часть ступени, а не вторая настройка рядом.
        var presetButton = new Button();
        void UpdatePresetButton()
        {
            presetButton.text =
                _loc.Get("settings.graphics.overall_quality") + ": " +
                _loc.Get(SettingSchema.LabelOf(_graphicsSettings.SelectedPreset));
        }

        presetButton.clicked += () =>
        {
            GraphicsPreset next = _graphicsSettings.SelectedPreset == GraphicsPreset.Standard
                ? GraphicsPreset.Overdrive
                : GraphicsPreset.Standard;
            _graphicsSettings.SelectPreset(next);
            _refreshAll();
        };
        presetButton.AddToClassList("pause-btn");
        _refreshers.Add(UpdatePresetButton);
        UpdatePresetButton();
        graphicsSection.Add(presetButton);

        Toggle distortionToggle = PauseMenuUIFactory.CreateBoundToggle(
            _loc.Get("settings.world.block_edge_distortion"),
            () => _clientConfig.Config.Terrain.EnableDistortion,
            value => _graphicsSettings.UpdateWorldMaterialSettings(
                config => config.Terrain.EnableDistortion = value),
            _refreshers);
        graphicsSection.Add(distortionToggle);

        var distortionStyleRow = new VisualElement();
        distortionStyleRow.AddToClassList("pause-slider-container");
        var distortionStyleLabel = new Label(_loc.Get("settings.world.distortion_style"));
        distortionStyleLabel.AddToClassList("pause-slider-label");
        distortionStyleRow.Add(distortionStyleLabel);
        var distortionStyleDropdown = new DropdownField
        {
            choices = new List<string>
            {
                _loc.Get("settings.world.distortion_style.classic"),
                _loc.Get("settings.world.distortion_style.organic"),
            },
        };
        distortionStyleDropdown.index = (int)_clientConfig.Config.Terrain.DistortionStyle;
        distortionStyleDropdown.RegisterValueChangedCallback(_ =>
        {
            TerrainDistortionStyle style = (TerrainDistortionStyle)distortionStyleDropdown.index;
            _graphicsSettings.UpdateWorldMaterialSettings(
                config => config.Terrain.DistortionStyle = style);
        });
        _refreshers.Add(() =>
        {
            distortionStyleDropdown.index = (int)_clientConfig.Config.Terrain.DistortionStyle;
        });
        distortionStyleRow.Add(distortionStyleDropdown);
        graphicsSection.Add(distortionStyleRow);

        return graphicsScroll;
    }
}
