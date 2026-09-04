#nullable enable

namespace Fodinae.Rendering.PostProcessing;

/// <summary>
/// Неизменяемый снимок продвинутых эффектов, который читает проход рендера.
/// </summary>
/// <remarks>
/// Раньше рядом жил мутабельный AdvancedPostProcessSettings: его хранил
/// клиентский конфиг, ползунки настроек писали в него по полю, а перед
/// отправкой в проход он копировался сюда. С переходом на тумблеры конфиг
/// хранит десять bool, снимок собирает AdvancedPostProcessComposer из
/// PostProcessLook, и промежуточный мутабельный класс стал пустым звеном.
/// </remarks>
public readonly record struct AdvancedPostProcessSnapshot(
    float LocalContrastIntensity,
    float LensDirtIntensity,
    float LensDirtScale,
    float AnamorphicIntensity,
    float AnamorphicLength,
    float ChromaticDiffractionIntensity,
    float HeatRefractionIntensity,
    float HeatRefractionScale,
    float GlintIntensity,
    float GlintThreshold,
    float VolumetricDustIntensity,
    float VolumetricDustScale,
    float VolumetricDustSpeed,
    float PhosphorMaskIntensity,
    float DitheringIntensity,
    float TemporalPersistenceIntensity,
    float TemporalPersistenceDecay,
    float LightStability)
{
    public bool HasCurrentFrameEffects =>
        LocalContrastIntensity > 0f ||
        LensDirtIntensity > 0f ||
        AnamorphicIntensity > 0f ||
        ChromaticDiffractionIntensity > 0f ||
        HeatRefractionIntensity > 0f ||
        GlintIntensity > 0f ||
        VolumetricDustIntensity > 0f ||
        PhosphorMaskIntensity > 0f ||
        DitheringIntensity > 0f;

    public bool HasAnyEffects =>
        HasCurrentFrameEffects ||
        TemporalPersistenceIntensity > 0f ||
        LightStability > 0f;

    public bool RequiresBloomTexture =>
        LensDirtIntensity > 0f ||
        AnamorphicIntensity > 0f ||
        ChromaticDiffractionIntensity > 0f;
}
