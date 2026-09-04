#nullable enable

using System;
using UnityEngine;

namespace Fodinae.Core;

/// <summary>
/// Плоская форма конфига схемы 21 и старее — только для чтения при миграции.
/// </summary>
/// <remarks>
/// ЗАЧЕМ ОТДЕЛЬНЫЙ ТИП. До схемы 22 поля света, террейна и эффектов лежали
/// прямо в корне конфига. После разбиения на секции <c>JsonUtility</c> их
/// просто не видит: имена в файле не совпадают ни с одним полем нового
/// <see cref="ClientConfig"/>, и значения игрока молча пропали бы, а стандартный
/// пресет заодно перестал бы совпадать с авторскими значениями.
///
/// Поэтому старый файл разбирается дважды: новым типом (секции, метаданные) и
/// этим (плоский хвост), после чего <see cref="ClientConfigMigration"/>
/// раскладывает хвост по секциям.
///
/// Класс намеренно неполон: в нём только те поля, которые переезжают. Он
/// заморожен формой файла версии 21 и не меняется вместе с
/// <see cref="ClientConfig"/> — это снимок прошлого, а не действующая схема.
/// </remarks>
[Serializable]
internal sealed class ClientConfigLegacySchema21
{
    public int SchemaVersion;

    public bool AmbientOcclusionEnabled;
    public bool DiffuseBounceEnabled;
    public float AmbientIntensity;
    public float EmissionScale;
    public Color AmbientColor;
    public Color EmptyExtinctionRgb;
    public Color SolidExtinctionRgb;
    public float EmptyExtinctionMultiplier;
    public float SolidExtinctionMultiplier;
    public float BounceStrength;
    public float AmbientOcclusionRadiusCells;
    public float AmbientOcclusionStrength;
    public float MaximumLightMultiplier;
    public bool EnableFinalLightingClamp;
    public float TransmittanceDebugDistanceCells;
    public float MinimumTransmission;
    public int LightSafeBorder;
    public float DynamicLightIntensity;
    public Color DynamicLightColor;
    public float DynamicLightUpdatesPerSecond;

    public Vector2 TerrainFlowScale;
    public float TerrainShimmerSpeedScale;
    public float TerrainPulseSpeedScale;
    public Color TerrainShimmerColor;
    public Color TerrainDebugColor;
    public bool TerrainDebugMode;
    public bool EnableTerrainDistortion;
    public Color TransitEmissionColor;
    public float TransitEmissionStrength;
    public Color PerspectiveEmissionColor;
    public float PerspectiveEmissionStrength;
    public float SurfaceOccupancy;

    public bool BloomEnabled;
    public bool VignetteEnabled;
    public bool ChromaticAberrationEnabled;
    public bool FilmGrainEnabled;
    public bool MotionBlurEnabled;
    public bool LocalContrastEnabled;
    public bool LensEffectsEnabled;
    public bool AtmosphereEnabled;
    public bool DisplayPhysicsEnabled;
    public bool TemporalEnabled;

    public WorldLightingSettings ToLighting() => new()
    {
        DiffuseBounceEnabled = DiffuseBounceEnabled,
        AmbientIntensity = AmbientIntensity,
        EmissionScale = EmissionScale,
        AmbientColor = AmbientColor,
        EmptyExtinctionRgb = EmptyExtinctionRgb,
        SolidExtinctionRgb = SolidExtinctionRgb,
        EmptyExtinctionMultiplier = EmptyExtinctionMultiplier,
        SolidExtinctionMultiplier = SolidExtinctionMultiplier,
        BounceStrength = BounceStrength,
        MaximumLightMultiplier = MaximumLightMultiplier,
        EnableFinalLightingClamp = EnableFinalLightingClamp,
        TransmittanceDebugDistanceCells = TransmittanceDebugDistanceCells,
        MinimumTransmission = MinimumTransmission,
        LightSafeBorder = LightSafeBorder,
        DynamicLightIntensity = DynamicLightIntensity,
        DynamicLightColor = DynamicLightColor,
        DynamicLightUpdatesPerSecond = DynamicLightUpdatesPerSecond,
    };

    public TerrainSettings ToTerrain() => new()
    {
        FlowScale = TerrainFlowScale,
        ShimmerSpeedScale = TerrainShimmerSpeedScale,
        PulseSpeedScale = TerrainPulseSpeedScale,
        ShimmerColor = TerrainShimmerColor,
        DebugColor = TerrainDebugColor,
        DebugMode = TerrainDebugMode,
        EnableDistortion = EnableTerrainDistortion,
        TransitEmissionColor = TransitEmissionColor,
        TransitEmissionStrength = TransitEmissionStrength,
        PerspectiveEmissionColor = PerspectiveEmissionColor,
        PerspectiveEmissionStrength = PerspectiveEmissionStrength,
        SurfaceOccupancy = SurfaceOccupancy,
    };

    public EffectSettings ToEffects() => new()
    {
        BloomEnabled = BloomEnabled,
        VignetteEnabled = VignetteEnabled,
        ChromaticAberrationEnabled = ChromaticAberrationEnabled,
        FilmGrainEnabled = FilmGrainEnabled,
        MotionBlurEnabled = MotionBlurEnabled,
        LocalContrastEnabled = LocalContrastEnabled,
        LensEffectsEnabled = LensEffectsEnabled,
        AtmosphereEnabled = AtmosphereEnabled,
        DisplayPhysicsEnabled = DisplayPhysicsEnabled,
        TemporalEnabled = TemporalEnabled,
    };
}
