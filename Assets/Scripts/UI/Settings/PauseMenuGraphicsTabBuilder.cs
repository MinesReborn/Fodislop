#nullable enable

using System;
using System.Collections.Generic;
using Fodinae.Core;
using Fodinae.Core.Interfaces;
using Fodinae.Core.Localization;
using Fodinae.Rendering;
using Fodinae.World.Lighting;
using Fodinae.World.Lighting.Quality;
using UnityEngine;
using UnityEngine.UIElements;

namespace Fodinae.UI;

/// <summary>
/// Builds the Graphics tab in the Pause Menu.
/// </summary>
internal sealed class PauseMenuGraphicsTabBuilder
{
    private readonly GraphicsSettingsController _graphicsSettings;
    private readonly LightingEngine _lightingEngine;
    private readonly IClientConfigManager _clientConfig;
    private readonly ICollection<Action> _refreshers;
    private readonly ILocalizationService _loc;
    private readonly Action _refreshAll;
    private readonly Action<Action> _registerUpdateQualityButton;
    private readonly Action<Foldout> _registerCustomGraphicsSection;

    public PauseMenuGraphicsTabBuilder(
        GraphicsSettingsController graphicsSettings,
        LightingEngine lightingEngine,
        IClientConfigManager clientConfig,
        ICollection<Action> refreshers,
        ILocalizationService loc,
        Action refreshAll,
        Action<Action> registerUpdateQualityButton,
        Action<Foldout> registerCustomGraphicsSection)
    {
        _graphicsSettings = graphicsSettings;
        _lightingEngine = lightingEngine;
        _clientConfig = clientConfig;
        _refreshers = refreshers;
        _loc = loc;
        _refreshAll = refreshAll;
        _registerUpdateQualityButton = registerUpdateQualityButton;
        _registerCustomGraphicsSection = registerCustomGraphicsSection;
    }

    public VisualElement Build(ScrollView graphicsScroll)
    {
        VisualElement graphicsSection = graphicsScroll.Q<VisualElement>("GraphicsSection") ??
            throw new InvalidOperationException("[PauseMenu] GraphicsSection is missing from PauseMenu.uxml.");

        var lightingQuality = new Button();
        void UpdateLightingQualityButton()
        {
            GraphicsPreset selectedPreset = _graphicsSettings.SelectedPreset;
            lightingQuality.text =
                _loc.Get("settings.graphics.overall_quality") + ": " +
                _loc.Get(SettingSchema.LabelOf(selectedPreset));
        }

        _registerUpdateQualityButton(UpdateLightingQualityButton);

        Foldout? customGraphicsSection = null;

        lightingQuality.clicked += () =>
        {
            GraphicsPreset currentPreset = _graphicsSettings.SelectedPreset;
            GraphicsPreset nextPreset;
            if (GraphicsQualityProfile.IsStandard(currentPreset))
            {
                nextPreset = currentPreset == GraphicsPreset.Ultra
                    ? GraphicsPreset.Custom
                    : (GraphicsPreset)((int)currentPreset + 1);
            }
            else
            {
                nextPreset = GraphicsPreset.VeryLow;
            }

            if (nextPreset == GraphicsPreset.Custom)
            {
                _graphicsSettings.SelectCustomPreset();
                if (customGraphicsSection != null)
                {
                    customGraphicsSection.value = true;
                }
            }
            else
            {
                _graphicsSettings.SelectStandardPreset(nextPreset);
            }

            _refreshAll();
        };
        lightingQuality.AddToClassList("pause-btn");
        _refreshers.Add(UpdateLightingQualityButton);
        UpdateLightingQualityButton();
        graphicsSection.Add(lightingQuality);

        var lightingQualityTierButton = new Button();
        void UpdateLightingQualityTierButton()
        {
            GraphicsPreset preset = _graphicsSettings.SelectedPreset;
            LightingQualityMode mode = preset == GraphicsPreset.Custom
                ? _graphicsSettings.CustomSettings.LightingQuality
                : _lightingEngine.ActiveLightingQuality;
            lightingQualityTierButton.text =
                _loc.Get("settings.lighting.quality_label") + ": " +
                _loc.Get(SettingSchema.LabelOf(mode));
            lightingQualityTierButton.SetEnabled(preset == GraphicsPreset.Custom);
        }

        void ApplyCustomTechnicalSettings(Func<GraphicsQualitySettings, GraphicsQualitySettings> update)
        {
            _graphicsSettings.MarkCustom();
            GraphicsQualitySettings settings = update(_graphicsSettings.CustomSettings);
            _graphicsSettings.SetCustomSettings(settings);
            if (customGraphicsSection != null)
            {
                customGraphicsSection.value = true;
            }

            UpdateLightingQualityButton();
            UpdateLightingQualityTierButton();
        }

        lightingQualityTierButton.clicked += () =>
        {
            if (_graphicsSettings.SelectedPreset != GraphicsPreset.Custom)
            {
                return;
            }

            ApplyCustomTechnicalSettings(settings =>
            {
                settings.LightingQuality = settings.LightingQuality switch
                {
                    LightingQualityMode.Off => LightingQualityMode.PerBlock,
                    LightingQualityMode.PerBlock => LightingQualityMode.PerPixel,
                    LightingQualityMode.PerPixel => LightingQualityMode.PerPixelBilinearFix,
                    _ => LightingQualityMode.Off,
                };
                return settings;
            });
            UpdateLightingQualityTierButton();
        };
        lightingQualityTierButton.AddToClassList("pause-btn");
        _refreshers.Add(UpdateLightingQualityTierButton);
        UpdateLightingQualityTierButton();
        graphicsSection.Add(lightingQualityTierButton);

        Toggle distortionToggle = PauseMenuUIFactory.CreateBoundToggle(
            _loc.Get("settings.world.block_edge_distortion"),
            () => _clientConfig.Config.Terrain.EnableDistortion,
            value => _graphicsSettings.UpdateCustomWorldMaterialSettings(
                config => config.Terrain.EnableDistortion = value),
            _refreshers);
        graphicsSection.Add(distortionToggle);

        customGraphicsSection = new Foldout
        {
            text = _loc.Get("settings.graphics.custom_profile"),
            value = _graphicsSettings.SelectedPreset == GraphicsPreset.Custom,
        };
        customGraphicsSection.AddToClassList("settings-section");
        customGraphicsSection.AddToClassList("settings-section--custom");
        _registerCustomGraphicsSection(customGraphicsSection);

        var customGraphicsButton = new Button
        {
            text = _loc.Get("settings.graphics.customize"),
        };
        customGraphicsButton.AddToClassList("pause-btn");
        customGraphicsButton.clicked += () =>
        {
            _graphicsSettings.SelectCustomPreset();
            customGraphicsSection.value = true;
            _refreshAll();
        };
        graphicsSection.Add(customGraphicsButton);

        VisualElement CreateTechnicalSlider(
            string fieldName,
            Func<float> read,
            Func<GraphicsQualitySettings, float, GraphicsQualitySettings> apply)
        {
            SettingRangeAttribute range = SettingSchema.RangeOf(typeof(GraphicsQualitySettings), fieldName);
            string label = _loc.Get(SettingSchema.LabelOf(typeof(GraphicsQualitySettings), fieldName));
            return PauseMenuUIFactory.CreateBoundSlider(
                label,
                read,
                value => ApplyCustomTechnicalSettings(settings => apply(settings, value)),
                range.Minimum,
                range.Maximum,
                _refreshers);
        }

        customGraphicsSection.Add(CreateTechnicalSlider(
            nameof(GraphicsQualitySettings.LightingMinimumPixelsPerCell),
            () => _graphicsSettings.CustomSettings.LightingMinimumPixelsPerCell,
            (settings, value) =>
            {
                settings.LightingMinimumPixelsPerCell = Mathf.RoundToInt(value);
                return settings;
            }));
        customGraphicsSection.Add(CreateTechnicalSlider(
            nameof(GraphicsQualitySettings.LightingMaximumTextureDimension),
            () => _graphicsSettings.CustomSettings.LightingMaximumTextureDimension,
            (settings, value) =>
            {
                settings.LightingMaximumTextureDimension = Mathf.RoundToInt(value);
                return settings;
            }));
        customGraphicsSection.Add(CreateTechnicalSlider(
            nameof(GraphicsQualitySettings.LightingMaximumLightCount),
            () => _graphicsSettings.CustomSettings.LightingMaximumLightCount,
            (settings, value) =>
            {
                settings.LightingMaximumLightCount = Mathf.RoundToInt(value);
                return settings;
            }));
        customGraphicsSection.Add(CreateTechnicalSlider(
            nameof(GraphicsQualitySettings.LightingMaximumRaySteps),
            () => _graphicsSettings.CustomSettings.LightingMaximumRaySteps,
            (settings, value) =>
            {
                settings.LightingMaximumRaySteps = Mathf.RoundToInt(value);
                return settings;
            }));

        customGraphicsSection.Add(CreateTechnicalSlider(
            nameof(GraphicsQualitySettings.LightingUpdatesPerSecond),
            () => _graphicsSettings.CustomSettings.LightingUpdatesPerSecond,
            (settings, value) =>
            {
                settings.LightingUpdatesPerSecond = Mathf.Round(value);
                return settings;
            }));
        customGraphicsSection.Add(CreateTechnicalSlider(
            nameof(GraphicsQualitySettings.LightingCascadeAtlasLimit),
            () => _graphicsSettings.CustomSettings.LightingCascadeAtlasLimit,
            (settings, value) =>
            {
                settings.LightingCascadeAtlasLimit = Mathf.RoundToInt(value);
                return settings;
            }));
        customGraphicsSection.Add(CreateTechnicalSlider(
            nameof(GraphicsQualitySettings.RenderScale),
            () => _graphicsSettings.CustomSettings.RenderScale,
            (settings, value) =>
            {
                settings.RenderScale = value;
                return settings;
            }));

        Button customAntiAliasingButton = PauseMenuUIFactory.CreateBoundCycleButton(
            () =>
            {
                int aa = _graphicsSettings.CustomSettings.AntiAliasing;
                string valueText = aa <= 0 ? "Off" : $"{aa}x";
                return $"MSAA: {valueText}";
            },
            () => ApplyCustomTechnicalSettings(settings =>
            {
                // Список допустимых значений один и живёт над самой
                // настройкой: своя лесенка здесь разошлась бы с проверкой
                // при первом же изменении набора.
                int[] steps = GraphicsQualitySettings.AntiAliasingSampleCounts;
                int current = System.Array.IndexOf(steps, settings.AntiAliasing);
                settings.AntiAliasing = steps[(current + 1) % steps.Length];
                return settings;
            }),
            _refreshers);
        customGraphicsSection.Add(customAntiAliasingButton);

        graphicsSection.Add(customGraphicsSection);

        void MarkGraphicsCustom()
        {
            _graphicsSettings.MarkCustom();
            UpdateLightingQualityButton();
        }

        Toggle globalIlluminationToggle = PauseMenuUIFactory.CreateBoundToggle(
            _loc.Get("settings.advanced.diffuse_bounce"),
            () => _lightingEngine.DiffuseBounceEnabled,
            value =>
            {
                MarkGraphicsCustom();
                _lightingEngine.SetDiffuseBounceEnabled(value);
            },
            _refreshers);
        graphicsSection.Add(globalIlluminationToggle);

        return graphicsScroll;
    }
}
