#nullable enable

using UnityEngine;
using UnityEngine.UIElements;

namespace Kern.UI;

public static class DynamicAtlasConfigurator
{
    public const int TargetMaxSubTextureSize = 1024;
    public const int TargetMaxAtlasSize = 4096;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    public static void Initialize()
    {
        ApplyToAllLoaded();
    }

    public static void ApplyToAllLoaded()
    {
        PanelSettings[] panelSettingsArray = Resources.FindObjectsOfTypeAll<PanelSettings>();
        foreach (PanelSettings panelSettings in panelSettingsArray)
        {
            Apply(panelSettings);
        }
    }

    public static void Apply(PanelSettings? panelSettings)
    {
        if (panelSettings == null)
        {
            return;
        }

        DynamicAtlasSettings settings = panelSettings.dynamicAtlasSettings;
        settings.maxSubTextureSize = TargetMaxSubTextureSize;
        settings.maxAtlasSize = TargetMaxAtlasSize;
        settings.activeFilters = DynamicAtlasFilters.Format | DynamicAtlasFilters.ColorSpace | DynamicAtlasFilters.FilterMode;
        settings.customFilter = static (Texture2D _, ref DynamicAtlasFilters filters) =>
        {
            filters &= ~(DynamicAtlasFilters.Readability | DynamicAtlasFilters.Size);
            return true;
        };
        panelSettings.dynamicAtlasSettings = settings;
    }
}
