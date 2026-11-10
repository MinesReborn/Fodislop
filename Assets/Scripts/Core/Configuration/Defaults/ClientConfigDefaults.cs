#nullable enable

using System;

using Kern.Rendering;

using Kern.Core.UserInterface;
using Kern.Core.Platform;

namespace Kern.Core.Configuration;

internal static class ClientConfigDefaults
{
    public static ClientConfig Create(
        GraphicsQualityProfile graphicsQualityProfile,
        GraphicsPreset initialGraphicsPreset = GraphicsPreset.Standard)
    {
        if (graphicsQualityProfile == null)
        {
            throw new ArgumentNullException(nameof(graphicsQualityProfile));
        }

        var config = new ClientConfig
        {
            SchemaVersion = ClientConfig.CurrentSchemaVersion,
            GraphicsPreset = initialGraphicsPreset,
        };
        config.GraphicsQualitySettings = graphicsQualityProfile.Get(config.GraphicsPreset);
        config.Interface.UIScale = UIScalePolicy.RecommendedDefaultScale;
        config.Interface.Language = ClientMarketPolicy.DefaultLanguageCode;
        return config;
    }
}
