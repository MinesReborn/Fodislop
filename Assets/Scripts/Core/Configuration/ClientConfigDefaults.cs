#nullable enable

using System;
using System.IO;
using Fodinae.Rendering;

namespace Fodinae.Core;

/// <summary>
/// Свежий конфиг из авторских значений.
/// </summary>
/// <remarks>
/// ЗАЧЕМ ТАК КОРОТКО. Раньше здесь было сто строк присваиваний вида
/// <c>AmbientIntensity = lighting.AmbientIntensity</c>, переписывающих поле за
/// полем снимок `ProjectDefaults.asset` в конфиг, плюс ещё два таких же блока
/// для частичного сброса. Значение по умолчанию было отдельной сущностью,
/// которую надо было доставить до поля вручную.
///
/// Теперь значение по умолчанию — это инициализатор поля секции, так что
/// «конфиг по умолчанию» и есть <c>new ClientConfig()</c>. Остаётся ровно то,
/// что из инициализатора не выводится: технические параметры пресета графики
/// живут в отдельном ассете шести пресетов и зависят от выбранного пресета.
/// </remarks>
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
        return config;
    }

    public static GraphicsPreset ConvertLegacyGraphicsQuality(int legacyQuality)
    {
        return legacyQuality switch
        {
            0 => GraphicsPreset.Low,
            1 => GraphicsPreset.Medium,
            2 => GraphicsPreset.High,
            3 => GraphicsPreset.Ultra,
            _ => throw new InvalidDataException(
                $"Legacy graphics quality '{legacyQuality}' is outside the supported range 0..3."),
        };
    }
}
