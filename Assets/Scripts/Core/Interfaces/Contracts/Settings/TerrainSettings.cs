#nullable enable

using System;
using UnityEngine;

namespace Fodinae.Core;

/// <summary>
/// Материал террейна и закартовых поверхностей: анимация, отладка, эмиссия.
/// </summary>
[Serializable]
public sealed class TerrainSettings
{
    [SettingRange(0.001f, 1024f)]
    [SettingConsumer(SettingConsumerTarget.TerrainRenderer, "TerrainMaterialManager.FlowScale")]
    public Vector2 FlowScale = new(12f, 10f);

    [SettingRange(0f, 10f)]
    [SettingLabel("settings.world.shimmer_speed")]
    [SettingConsumer(SettingConsumerTarget.TerrainRenderer, "TerrainMaterialManager.ShimmerSpeedScale")]
    public float ShimmerSpeedScale = 0.05f;

    [SettingRange(0f, 10f)]
    [SettingLabel("settings.world.pulse_speed")]
    [SettingConsumer(SettingConsumerTarget.TerrainRenderer, "TerrainMaterialManager.PulseSpeedScale")]
    public float PulseSpeedScale = 0.5f;

    [SettingLabel("settings.world.shimmer_color")]
    [SettingUnbounded("Цвет: компоненты проверяются на конечность и неотрицательность, отрезка нет — яркость выше единицы законна.")]
    [SettingConsumer(SettingConsumerTarget.TerrainRenderer, "TerrainMaterialManager.ShimmerColor")]
    public Color ShimmerColor = Color.white;

    [SettingUnbounded("Цвет: компоненты проверяются на конечность и неотрицательность, отрезка нет — яркость выше единицы законна.")]
    [SettingConsumer(SettingConsumerTarget.TerrainRenderer, "TerrainMaterialManager.DebugColor")]
    public Color DebugColor = Color.magenta;

    [SettingUnbounded("Тумблер отладочной раскраски террейна.")]
    [SettingConsumer(SettingConsumerTarget.TerrainRenderer, "TerrainMaterialManager.DebugMode")]
    public bool DebugMode;

    [SettingUnbounded("Тумблер искажения кромки блока.")]
    [SettingLabel("settings.world.block_edge_distortion")]
    [SettingConsumer(SettingConsumerTarget.TerrainRenderer, "TerrainRenderer._precalc.EnableDistortion")]
    public bool EnableDistortion = true;

    [SettingLabel("settings.world.surface_emission_color")]
    [SettingUnbounded("Цвет: компоненты проверяются на конечность и неотрицательность, отрезка нет — яркость выше единицы законна.")]
    [SettingConsumer(SettingConsumerTarget.SurfaceRenderer, "SurfaceRenderer._materialManager.ApplyMaterialConfig")]
    public Color TransitEmissionColor = new(1f, 0.7f, 0.35f, 1f);

    [SettingRange(0f, 8f)]
    [SettingLabel("settings.world.surface_emission")]
    [SettingConsumer(SettingConsumerTarget.SurfaceRenderer, "SurfaceRenderer._materialManager.ApplyMaterialConfig")]
    public float TransitEmissionStrength = 0.35f;

    [SettingLabel("settings.world.far_surface_color")]
    [SettingUnbounded("Цвет: компоненты проверяются на конечность и неотрицательность, отрезка нет — яркость выше единицы законна.")]
    [SettingConsumer(SettingConsumerTarget.SurfaceRenderer, "SurfaceRenderer._materialManager.ApplyMaterialConfig")]
    public Color PerspectiveEmissionColor = new(0.45f, 0.65f, 1f, 1f);

    [SettingRange(0f, 8f)]
    [SettingLabel("settings.world.far_surface_emission")]
    [SettingConsumer(SettingConsumerTarget.SurfaceRenderer, "SurfaceRenderer._materialManager.ApplyMaterialConfig")]
    public float PerspectiveEmissionStrength = 0.12f;

    [SettingRange(0f, 1f)]
    [SettingConsumer(SettingConsumerTarget.SurfaceRenderer, "SurfaceRenderer._materialManager.ApplyMaterialConfig")]
    public float SurfaceOccupancy = 1f;
}
