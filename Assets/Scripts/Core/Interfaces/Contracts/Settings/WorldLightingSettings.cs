#nullable enable

using System;
using Fodinae.World.Lighting;
using UnityEngine;

namespace Fodinae.Core;

/// <summary>
/// Освещение мира: всё, что читает решатель радиансных каскадов.
/// </summary>
/// <remarks>
/// ЗНАЧЕНИЯ ЗДЕСЬ АВТОРСКИЕ. Инициализатор поля — и есть значение по
/// умолчанию: другого источника нет. Раньше он лежал в
/// `Resources/Configuration/ProjectDefaults.asset`, откуда переписывался в
/// снимок, из снимка в конфиг, и всё это надо было держать в согласии руками.
/// </remarks>
[Serializable]
public sealed class WorldLightingSettings
{
    [SettingUnbounded("Тумблер диффузного отскока.")]
    [SettingConsumer(SettingConsumerTarget.LightingEngine, "LightingEngine.SetDiffuseBounceEnabled -> _bounceDirty")]
    public bool DiffuseBounceEnabled = true;

    public const float DefaultAmbientIntensity = 0.08f;
    public const float DefaultEmissionScale = 2f;

    [SettingRange(0f, 1f)]
    [SettingLabel("settings.advanced.ambient_intensity")]
    [SettingConsumer(SettingConsumerTarget.LightingEngine, "LightingEngine.SetAmbientIntensity -> _compositeDirty")]
    public float AmbientIntensity = DefaultAmbientIntensity;

    [SettingRange(0.1f, 8f)]
    [SettingLabel("settings.advanced.emission_power")]
    [SettingConsumer(SettingConsumerTarget.LightingEngine, "LightingEngine.SetEmissionScale -> _fieldDirty, _compositeDirty")]
    public float EmissionScale = DefaultEmissionScale;

    [SettingLabel("settings.advanced.ambient_color")]
    [SettingUnbounded("Цвет: компоненты проверяются на конечность и неотрицательность, отрезка нет — яркость выше единицы законна.")]
    [SettingConsumer(SettingConsumerTarget.LightingEngine, "LightingEngine.SetAmbientColor -> _compositeDirty")]
    public Color AmbientColor = new(0.12f, 0.14f, 0.18f, 1f);

    [SettingLabel("settings.advanced.empty_extinction")]
    [SettingUnbounded("Цвет: компоненты проверяются на конечность и неотрицательность, отрезка нет — яркость выше единицы законна.")]
    [SettingConsumer(SettingConsumerTarget.LightingEngine, "LightingEngine.SetEmptyExtinctionColor -> _fieldDirty, _compositeDirty")]
    public Color EmptyExtinctionRgb = new(0.015f, 0.012f, 0.009f, 1f);

    // Компоненты выше единицы — это не ошибка: плотное вещество гасит красный
    // сильнее синего, и множитель поглощения не ограничен единицей.
    [SettingLabel("settings.advanced.solid_extinction")]
    [SettingUnbounded("Цвет: компоненты проверяются на конечность и неотрицательность, отрезка нет — яркость выше единицы законна.")]
    [SettingConsumer(SettingConsumerTarget.LightingEngine, "LightingEngine.SetSolidExtinctionColor -> _fieldDirty, _compositeDirty")]
    public Color SolidExtinctionRgb = new(1.2f, 1.1f, 1f, 1f);

    [SettingRange(0f, 2f)]
    [SettingLabel("settings.advanced.empty_extinction_falloff")]
    [SettingConsumer(SettingConsumerTarget.LightingEngine, "LightingEngine.SetEmptyExtinctionMultiplier -> _fieldDirty, _compositeDirty")]
    public float EmptyExtinctionMultiplier = 1f;

    [SettingRange(0.25f, 2f)]
    [SettingLabel("settings.advanced.solid_extinction_falloff")]
    [SettingConsumer(SettingConsumerTarget.LightingEngine, "LightingEngine.SetSolidExtinctionMultiplier -> _fieldDirty, _compositeDirty")]
    public float SolidExtinctionMultiplier = 2f;

    [SettingRange(0f, 1f)]
    [SettingLabel("settings.advanced.bounce_strength")]
    [SettingConsumer(SettingConsumerTarget.LightingEngine, "LightingEngine.SetBounceStrength -> _bounceDirty, _compositeDirty")]
    public float BounceStrength = 1f;

    [SettingRange(0.25f, LightingConfigLimits.MaximumLightMultiplier)]
    [SettingLabel("settings.advanced.max_light_multiplier")]
    [SettingConsumer(SettingConsumerTarget.LightingEngine, "LightingEngine.SetMaximumLightMultiplier -> _compositeDirty")]
    public float MaximumLightMultiplier = 1f;

    [SettingUnbounded("Тумблер финального клампа света.")]
    [SettingLabel("settings.advanced.clamp_final_light")]
    [SettingConsumer(SettingConsumerTarget.LightingEngine, "LightingEngine.SetFinalLightingClampEnabled -> _compositeDirty")]
    public bool EnableFinalLightingClamp;

    [SettingRange(2f, 32f)]
    [SettingLabel("settings.advanced.transmittance_debug")]
    [SettingConsumer(SettingConsumerTarget.LightingEngine, "LightingEngine.SetTransmittanceDebugDistance -> _compositeDirty")]
    public float TransmittanceDebugDistanceCells = 10f;

    [SettingRange(0.0001f, 0.1f)]
    [SettingLabel("settings.advanced.min_transmission")]
    [SettingConsumer(SettingConsumerTarget.LightingEngine, "LightingEngine.SetMinimumTransmission -> _fieldDirty, _compositeDirty")]
    public float MinimumTransmission = 0.008f;

    [SettingRange(0f, 8f)]
    [SettingLabel("settings.advanced.light_safe_border")]
    [SettingConsumer(SettingConsumerTarget.LightingEngine, "LightingEngine.SetLightSafeBorder -> _fieldDirty, _compositeDirty")]
    public int LightSafeBorder = 2;

    [SettingRange(0f, 4f)]
    [SettingLabel("settings.advanced.player_emission_power")]
    [SettingConsumer(SettingConsumerTarget.LightingEngine, "LightingEngine.SetDynamicLightSettings -> MarkDirty, _compositeDirty")]
    public float DynamicLightIntensity = 1.25f;

    [SettingUnbounded("Цвет: компоненты проверяются на конечность и неотрицательность, отрезка нет — яркость выше единицы законна.")]
    [SettingConsumer(SettingConsumerTarget.LightingEngine, "LightingEngine.SetDynamicLightSettings -> MarkDirty, _compositeDirty")]
    public Color DynamicLightColor = Color.white;

    [SettingRange(1f, LightingConfigLimits.DynamicLightUpdatesPerSecond)]
    [SettingLabel("settings.advanced.dynamic_emission_rate")]
    [SettingConsumer(SettingConsumerTarget.LightingEngine, "LightingEngine.SetDynamicLightUpdatesPerSecond -> _nextDynamicLightingUpdateTime")]
    public float DynamicLightUpdatesPerSecond = 20f;
}
