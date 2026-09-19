#nullable enable

using System;

using Kern.Rendering;

namespace Kern.Core;

internal static class ClientConfigDefaults
{
    public static ClientConfig Create(GraphicsQualityProfile graphicsQualityProfile)
    {
        if (graphicsQualityProfile == null)
        {
            throw new ArgumentNullException(nameof(graphicsQualityProfile));
        }

        var config = new ClientConfig
        {
            SchemaVersion = ClientConfig.CurrentSchemaVersion,
        };
        config.GraphicsQualitySettings = graphicsQualityProfile.Get(config.GraphicsPreset);
        config.Interface.UIScale = UIScaleUtility.RecommendedDefaultScale;
        return config;
    }
}
