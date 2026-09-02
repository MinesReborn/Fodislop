#nullable enable

using System;
using System.Collections.Generic;
using Fodinae.Core;
using Fodinae.Core.Interfaces;
using Fodinae.Core.Localization;
using Fodinae.Rendering;
using Fodinae.Rendering.PostProcessing;
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

        string[] graphicsPresetNames =
        [
            "settings.preset.very_low",
            "settings.preset.low",
            "settings.preset.medium",
            "settings.preset.high",
            "settings.preset.very_high",
            "settings.preset.ultra",
            "settings.preset.custom",
        ];
        var lightingQuality = new Button();
        void UpdateLightingQualityButton()
        {
            GraphicsPreset selectedPreset = _graphicsSettings.SelectedPreset;
            lightingQuality.text =
                _loc.Get("settings.graphics.overall_quality") + ": " +
                _loc.Get(graphicsPresetNames[(int)selectedPreset]);
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

        string[] lightingQualityTierNames =
        [
            "settings.lighting.per_block",
            "settings.lighting.off",
            "settings.lighting.per_pixel",
            "settings.lighting.per_pixel_bilinear",
        ];
        var lightingQualityTierButton = new Button();
        void UpdateLightingQualityTierButton()
        {
            GraphicsPreset preset = _graphicsSettings.SelectedPreset;
            LightingQualityMode mode = preset == GraphicsPreset.Custom
                ? _graphicsSettings.CustomSettings.LightingQuality
                : _lightingEngine.ActiveLightingQuality;
            lightingQualityTierButton.text =
                _loc.Get("settings.lighting.quality_label") + ": " +
                _loc.Get(lightingQualityTierNames[(int)mode]);
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

        // Индексация массива по (int)mode тут не годится: значение 1 из
        // перечисления изъято, а Essential остался равным 2. Сопоставление
        // явное, чтобы изъятое значение не читалось как чужая строка.
        static string PostProcessTierKey(PostProcessQualityMode mode) => mode switch
        {
            PostProcessQualityMode.Essential => "settings.post_process.core",
            _ => "settings.post_process.full",
        };

        var postProcessTierButton = new Button();
        void UpdatePostProcessTierButton()
        {
            GraphicsPreset preset = _graphicsSettings.SelectedPreset;
            PostProcessQualityMode mode =
                _clientConfig.Config.GraphicsQualitySettings.PostProcessQuality;
            postProcessTierButton.text =
                _loc.Get("settings.post_process.quality_label") + ": " +
                _loc.Get(PostProcessTierKey(mode));
            postProcessTierButton.SetEnabled(preset == GraphicsPreset.Custom);
        }

        postProcessTierButton.clicked += () =>
        {
            if (_graphicsSettings.SelectedPreset != GraphicsPreset.Custom)
            {
                return;
            }

            ApplyCustomTechnicalSettings(settings =>
            {
                settings.PostProcessQuality =
                    settings.PostProcessQuality == PostProcessQualityMode.Full
                        ? PostProcessQualityMode.Essential
                        : PostProcessQualityMode.Full;
                return settings;
            });
            UpdatePostProcessTierButton();
        };
        postProcessTierButton.AddToClassList("pause-btn");
        _refreshers.Add(UpdatePostProcessTierButton);
        UpdatePostProcessTierButton();
        graphicsSection.Add(postProcessTierButton);

        Toggle distortionToggle = PauseMenuUIFactory.CreateBoundToggle(
            _loc.Get("settings.world.block_edge_distortion"),
            () => _clientConfig.Config.EnableTerrainDistortion,
            value => _graphicsSettings.UpdateCustomWorldMaterialSettings(
                config => config.EnableTerrainDistortion = value),
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

        customGraphicsSection.Add(PauseMenuUIFactory.CreateBoundSlider(
            _loc.Get("settings.lighting.density"),
            () => _graphicsSettings.CustomSettings.LightingMinimumPixelsPerCell,
            value => ApplyCustomTechnicalSettings(settings =>
            {
                settings.LightingMinimumPixelsPerCell = Mathf.RoundToInt(value);
                return settings;
            }),
            1f,
            8f,
            _refreshers));
        customGraphicsSection.Add(PauseMenuUIFactory.CreateBoundSlider(
            _loc.Get("settings.lighting.max_size"),
            () => _graphicsSettings.CustomSettings.LightingMaximumTextureDimension,
            value => ApplyCustomTechnicalSettings(settings =>
            {
                settings.LightingMaximumTextureDimension = Mathf.RoundToInt(value);
                return settings;
            }),
            GraphicsQualitySettings.MinimumLightingTextureDimension,
            4096f,
            _refreshers));
        customGraphicsSection.Add(PauseMenuUIFactory.CreateBoundSlider(
            _loc.Get("settings.lighting.max_dynamic_lights"),
            () => _graphicsSettings.CustomSettings.LightingMaximumLightCount,
            value => ApplyCustomTechnicalSettings(settings =>
            {
                settings.LightingMaximumLightCount = Mathf.RoundToInt(value);
                return settings;
            }),
            1f,
            2048f,
            _refreshers));
        customGraphicsSection.Add(PauseMenuUIFactory.CreateBoundSlider(
            _loc.Get("settings.lighting.cascade_steps"),
            () => _graphicsSettings.CustomSettings.LightingMaximumRaySteps,
            value => ApplyCustomTechnicalSettings(settings =>
            {
                settings.LightingMaximumRaySteps = Mathf.RoundToInt(value);
                return settings;
            }),
            1f,
            128f,
            _refreshers));
        customGraphicsSection.Add(PauseMenuUIFactory.CreateBoundSlider(
            _loc.Get("settings.lighting.solve_rate"),
            () => _graphicsSettings.CustomSettings.LightingUpdatesPerSecond,
            value => ApplyCustomTechnicalSettings(settings =>
            {
                settings.LightingUpdatesPerSecond = Mathf.Round(value);
                return settings;
            }),
            1f,
            60f,
            _refreshers));
        customGraphicsSection.Add(PauseMenuUIFactory.CreateBoundSlider(
            _loc.Get("settings.lighting.atlas_size"),
            () => _graphicsSettings.CustomSettings.LightingCascadeAtlasLimit,
            value => ApplyCustomTechnicalSettings(settings =>
            {
                settings.LightingCascadeAtlasLimit = Mathf.RoundToInt(value);
                return settings;
            }),
            128f,
            4096f,
            _refreshers));
        customGraphicsSection.Add(PauseMenuUIFactory.CreateBoundSlider(
            "Render scale",
            () => _graphicsSettings.CustomSettings.RenderScale,
            value => ApplyCustomTechnicalSettings(settings =>
            {
                settings.RenderScale = value;
                return settings;
            }),
            0.5f,
            1f,
            _refreshers));

        var customAntiAliasingButton = new Button();
        void RefreshCustomAntiAliasing()
        {
            customAntiAliasingButton.text =
                $"MSAA: {_graphicsSettings.CustomSettings.AntiAliasing}";
        }

        customAntiAliasingButton.clicked += () => ApplyCustomTechnicalSettings(settings =>
        {
            settings.AntiAliasing = settings.AntiAliasing switch
            {
                0 => 2,
                2 => 4,
                4 => 8,
                _ => 0,
            };
            return settings;
        });
        customAntiAliasingButton.AddToClassList("pause-btn");
        _refreshers.Add(RefreshCustomAntiAliasing);
        RefreshCustomAntiAliasing();
        customGraphicsSection.Add(customAntiAliasingButton);

        graphicsSection.Add(customGraphicsSection);

        void MarkGraphicsCustom()
        {
            _graphicsSettings.MarkCustom();
            UpdateLightingQualityButton();
        }

        Toggle ambientOcclusionToggle = PauseMenuUIFactory.CreateBoundToggle(
            _loc.Get("settings.advanced.contact_ao"),
            () => _lightingEngine.AmbientOcclusionEnabled,
            value =>
            {
                MarkGraphicsCustom();
                _lightingEngine.SetAmbientOcclusionEnabled(value);
            },
            _refreshers);
        graphicsSection.Add(ambientOcclusionToggle);

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
