#nullable enable

using System;
using System.IO;
using Fodinae.Rendering;
using UnityEngine;

namespace Fodinae.Core;

/// <summary>
/// Проверяет persisted данные без неявной подстановки defaults.
/// </summary>
/// <remarks>
/// ЗАЧЕМ ТАК КОРОТКО. Раньше здесь было ~50 рукописных
/// <c>ValidateFloat(config.X, min, max, …)</c> — по строке на настройку, с
/// границами, записанными литералами второй раз после
/// `ProjectDefaults.Validate` — и цепочка из сорока сравнений
/// <c>HasStandardGraphicsValues</c>. Обе конструкции надо было дописывать при
/// каждом новом поле, и забытая строка означала дыру, которую ничто не
/// показывало.
///
/// Диапазоны теперь объявлены над полями, поэтому обход по ним делает
/// <see cref="SettingSchema"/>. Здесь остаётся ровно то, что диапазоном не
/// выражается: перечни, парные правила и инварианты пресета графики.
/// </remarks>
internal sealed class ClientConfigValidator(GraphicsQualityProfile graphicsQualityProfile)
{
    private readonly GraphicsQualityProfile _graphicsQualityProfile = graphicsQualityProfile ??
        throw new ArgumentNullException(nameof(graphicsQualityProfile));

    public void Validate(ClientConfig config)
    {
        if (config == null)
        {
            throw new ArgumentNullException(nameof(config));
        }

        if (config.SchemaVersion != ClientConfig.CurrentSchemaVersion)
        {
            throw new InvalidDataException(
                $"Unsupported client config schema {config.SchemaVersion}; " +
                $"expected {ClientConfig.CurrentSchemaVersion}.");
        }

        SettingSchema.Validate(config.Audio);
        SettingSchema.Validate(config.Display);
        SettingSchema.Validate(config.Interface);
        SettingSchema.Validate(config.Accessibility);
        SettingSchema.Validate(config.Connection);
        SettingSchema.Validate(config.PostProcess);
        SettingSchema.Validate(config.Lighting);
        SettingSchema.Validate(config.Terrain);
        SettingSchema.Validate(config.Effects);

        ValidateDiscreteSettings(config);
        ValidateGraphics(config);
    }

    /// <summary>
    /// Правила, у которых нет отрезка: перечни, «ноль либо диапазон» и
    /// величины, осмысленные только в паре.
    /// </summary>
    private static void ValidateDiscreteSettings(ClientConfig config)
    {
        InterfaceSettings interfaceSettings = config.Interface;
        DisplaySettings display = config.Display;
        ConnectionSettings connection = config.Connection;
        if (interfaceSettings.Language is not ("ru" or "en" or "zh" or "zh-hant"))
        {
            throw new InvalidDataException(
                $"Client config value '{nameof(interfaceSettings.Language)}' is not a supported locale: " +
                $"'{interfaceSettings.Language}'.");
        }

        // Ширина и высота осмысленны только вместе: 0x0 значит «родной режим
        // дисплея», всё остальное обязано быть полной парой.
        bool usesCurrentResolution = display.ResolutionWidth == 0 && display.ResolutionHeight == 0;
        bool usesExplicitResolution = display.ResolutionWidth is >= 320 and <= 16384 &&
            display.ResolutionHeight is >= 200 and <= 16384;
        if (!usesCurrentResolution && !usesExplicitResolution)
        {
            throw new InvalidDataException(
                "Client resolution must be either 0x0 (current display mode) or a valid width and height.");
        }

        if (display.TargetFrameRate != -1 && display.TargetFrameRate is < 30 or > 1000)
        {
            throw new InvalidDataException(
                $"Client config value '{nameof(display.TargetFrameRate)}' must be -1 or within [30, 1000].");
        }

        if (!Enum.IsDefined(typeof(FullScreenMode), display.FullScreenMode))
        {
            throw new InvalidDataException(
                "Client config value 'FullScreenMode' must be a valid FullScreenMode value, " +
                $"got {display.FullScreenMode}.");
        }

        if (string.IsNullOrWhiteSpace(connection.ServerHost))
        {
            throw new InvalidDataException(
                "Client config value 'ServerHost' must be a non-empty host name or IP address.");
        }
    }

    private void ValidateGraphics(ClientConfig config)
    {
        if (!Enum.IsDefined(typeof(GraphicsPreset), config.GraphicsPreset))
        {
            throw new InvalidDataException(
                $"Unknown graphics preset value '{config.GraphicsPreset}'.");
        }

        try
        {
            GraphicsQualityProfile.ValidateSettings(
                config.GraphicsQualitySettings,
                config.GraphicsPreset.ToString());
        }
        catch (InvalidOperationException ex)
        {
            throw new InvalidDataException(
                "Client graphics quality settings are invalid.",
                ex);
        }

        if (!GraphicsQualityProfile.IsStandard(config.GraphicsPreset))
        {
            return;
        }

        if (config.GraphicsQualitySettings != _graphicsQualityProfile.Get(config.GraphicsPreset))
        {
            throw new InvalidDataException(
                $"Standard graphics preset '{config.GraphicsPreset}' was mutated in client config.");
        }

        // Стандартный пресет обязан совпадать с авторскими значениями во всех
        // секциях вида. Раньше это была цепочка из сорока сравнений, которую
        // забывали дополнять; теперь список полей берётся из объявления секции,
        // поэтому новое поле попадает под инвариант само.
        if (!SettingSchema.MatchesDefaults(config.Lighting) ||
            !SettingSchema.MatchesDefaults(config.Terrain) ||
            !SettingSchema.MatchesDefaults(config.Effects) ||
            !SettingSchema.MatchesDefaults(config.PostProcess))
        {
            throw new InvalidDataException(
                $"Standard graphics preset '{config.GraphicsPreset}' contains customized visual values. " +
                "Mark the preset as Custom before changing graphics settings.");
        }
    }
}
