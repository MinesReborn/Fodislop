#nullable enable

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace Kern.UI;

/// <summary>
/// Настройка динамического атласа UI Toolkit.
///
/// Атлас существует ради статичных текстур — иконок и шрифтов. Текстуры, которые
/// переписываются в рантайме, в него попадать не должны: атлас отдаёт для них
/// устаревшее содержимое, и карта с мини-картой выглядят «замершими», хотя CPU
/// каждый кадр пишет в них новые пиксели. Такие текстуры исключаются явной
/// регистрацией в <see cref="RegisterRuntimeReddrawn"/>.
/// </summary>
public static class DynamicAtlasConfigurator
{
    /// <summary>
    /// Предельный размер текстуры, попадающей в атлас. Равен размеру иконок
    /// клетки (32), поэтому инвентарь, корзина и панели продолжают батчиться,
    /// а крупные перерисовываемые текстуры не попадают в атлас.
    /// </summary>
    public const int TargetMaxSubTextureSize = 64;

    public const int TargetMaxAtlasSize = 4096;

    private static readonly HashSet<string> _runtimeRedrawnTextures = new();

    /// <summary>
    /// Исключает текстуру из динамического атласа. Вызывать сразу после создания:
    /// ключом служит имя текстуры, поэтому последующее переименование её обесценит.
    /// </summary>
    public static void RegisterRuntimeRedrawn(Texture2D texture)
    {
        if (texture == null)
        {
            return;
        }

        _runtimeRedrawnTextures.Add(texture.name);
    }

    public static bool IsRuntimeRedrawn(Texture2D texture) =>
        texture != null && _runtimeRedrawnTextures.Contains(texture.name);

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
        settings.customFilter = static (Texture2D texture, ref DynamicAtlasFilters filters) =>
        {
            if (IsRuntimeRedrawn(texture))
            {
                return false;
            }

            filters &= ~(DynamicAtlasFilters.Readability | DynamicAtlasFilters.Size);
            return true;
        };
        panelSettings.dynamicAtlasSettings = settings;
    }
}
