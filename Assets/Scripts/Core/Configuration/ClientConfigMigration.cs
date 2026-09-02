#nullable enable

using System;
using Fodinae.Core.Interfaces;
using Fodinae.Rendering;
using UnityEngine;

namespace Fodinae.Core;

internal sealed class ClientConfigMigration(
    IProjectDefaults projectDefaults,
    GraphicsQualityProfile graphicsQualityProfile)
{
    private readonly IProjectDefaults _projectDefaults = projectDefaults ??
        throw new ArgumentNullException(nameof(projectDefaults));
    private readonly GraphicsQualityProfile _graphicsQualityProfile = graphicsQualityProfile ??
        throw new ArgumentNullException(nameof(graphicsQualityProfile));

    public bool Migrate(ClientConfig config)
    {
        if (config == null)
        {
            throw new ArgumentNullException(nameof(config));
        }

        ShaderDefaultsSnapshot shaders = _projectDefaults.Shaders;
        bool migrated = false;
        if (config.SchemaVersion < 2)
        {
            ApplySchema2(config, shaders);
            migrated = true;
        }

        if (config.SchemaVersion < 4)
        {
            config.TerrainDebugColor = shaders.TerrainDebugColor;
            config.TerrainDebugMode = shaders.TerrainDebugMode;
            config.SchemaVersion = 4;
            migrated = true;
        }

        if (config.SchemaVersion < 5)
        {
            config.SchemaVersion = 5;
            migrated = true;
        }

        if (config.SchemaVersion < 6)
        {
            ClientConfigDefaults.ApplyShaderDefaults(config, shaders);
            config.ProjectDefaultsHash = _projectDefaults.ContentHash;
            config.SchemaVersion = 6;
            migrated = true;
        }

        if (config.SchemaVersion < 7)
        {
            config.ProjectDefaultsHash = _projectDefaults.ContentHash;
            config.SchemaVersion = 7;
            migrated = true;
        }

        if (config.SchemaVersion < 8)
        {
            ClientConfigDefaults.ApplyLightingDefaults(config, _projectDefaults.Lighting);
            config.SchemaVersion = 8;
            migrated = true;
        }

        if (config.SchemaVersion < 9)
        {
            GraphicsPreset previousPreset = ClientConfigDefaults.ConvertLegacyGraphicsQuality(
                (int)config.GraphicsPreset);
            config.GraphicsQualitySettings = _graphicsQualityProfile.Get(previousPreset);
            config.GraphicsPreset = GraphicsPreset.Custom;
            config.SchemaVersion = 9;
            migrated = true;
        }

        if (config.SchemaVersion < 10)
        {
            config.Connection.UseDummyConnection = ProjectRuntimeContracts.ClientConfiguration.DefaultUseDummyConnection;
            config.Connection.ServerHost = ProjectRuntimeContracts.ClientConfiguration.DefaultServerHost;
            config.Connection.ServerPort = ProjectRuntimeContracts.ClientConfiguration.DefaultServerPort;
            config.SchemaVersion = 10;
            migrated = true;
        }

        if (config.SchemaVersion < 11)
        {
            config.SchemaVersion = 11;
            migrated = true;
        }

        if (config.SchemaVersion < 12)
        {
            config.GraphicsQualitySettings.LightingMaximumTextureDimension =
                Mathf.Max(
                    config.GraphicsQualitySettings.LightingMaximumTextureDimension,
                    GraphicsQualitySettings.MinimumLightingTextureDimension);
            config.SchemaVersion = 12;
            migrated = true;
        }

        if (config.SchemaVersion < 16)
        {
            config.Display.HDREnabled = ProjectRuntimeContracts.ClientConfiguration.DefaultHDREnabled;
            config.SchemaVersion = 16;
            migrated = true;
        }

        if (config.SchemaVersion < 17)
        {
            config.SchemaVersion = 17;
            migrated = true;
        }

        if (config.SchemaVersion < 18)
        {
            config.SchemaVersion = 18;
            migrated = true;
        }

        if (config.SchemaVersion < 19)
        {
            // Величины постпроцесса уехали в PostProcessLook, в конфиге
            // остались тумблеры. Старые числа не переносятся: вид кадра
            // теперь авторский, а не то, куда игрок подвинул ползунок.
            ClientConfigDefaults.ApplyShaderDefaults(config, shaders);
            config.SchemaVersion = 19;
            migrated = true;
        }

        if (GraphicsQualityProfile.IsStandard(config.GraphicsPreset))
        {
            GraphicsQualitySettings standardSettings =
                _graphicsQualityProfile.Get(config.GraphicsPreset);
            if (config.GraphicsQualitySettings != standardSettings)
            {
                config.GraphicsQualitySettings = standardSettings;
                migrated = true;
            }
        }

        if (!string.Equals(
                config.ProjectDefaultsHash,
                _projectDefaults.ContentHash,
                StringComparison.Ordinal))
        {
            RefreshChangedProjectDefaults(config);
            migrated = true;
        }

        return migrated;
    }

    private static void ApplySchema2(ClientConfig config, ShaderDefaultsSnapshot shaders)
    {
        config.TerrainFlowScale = shaders.TerrainFlowScale;
        config.TerrainShimmerSpeedScale = shaders.TerrainShimmerSpeedScale;
        config.TerrainPulseSpeedScale = shaders.TerrainPulseSpeedScale;
        config.TerrainShimmerColor = shaders.TerrainShimmerColor;
        config.TerrainDebugColor = shaders.TerrainDebugColor;
        config.TerrainDebugMode = shaders.TerrainDebugMode;
        config.TransitEmissionColor = shaders.TransitEmissionColor;
        config.TransitEmissionStrength = shaders.TransitEmissionStrength;
        config.PerspectiveEmissionColor = shaders.PerspectiveEmissionColor;
        config.PerspectiveEmissionStrength = shaders.PerspectiveEmissionStrength;
        config.SurfaceOccupancy = shaders.SurfaceOccupancy;
        config.SchemaVersion = 2;
    }

    private void RefreshChangedProjectDefaults(ClientConfig config)
    {
        if (GraphicsQualityProfile.IsStandard(config.GraphicsPreset))
        {
            ClientConfigDefaults.ApplyLightingDefaults(config, _projectDefaults.Lighting);
            ClientConfigDefaults.ApplyShaderDefaults(config, _projectDefaults.Shaders);
            Debug.Log(
                "[ClientConfigMigration] ProjectDefaults changed; refreshed the selected " +
                "immutable standard graphics preset.");
        }
        else
        {
            Debug.Log(
                "[ClientConfigMigration] ProjectDefaults changed; preserved Custom graphics settings.");
        }

        config.ProjectDefaultsHash = _projectDefaults.ContentHash;
    }

}
