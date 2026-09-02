#nullable enable

using Fodinae.Core;
using Fodinae.Core.Interfaces;

namespace Fodinae.Rendering.PostProcessing;

/// <summary>
/// Собирает снимок продвинутых эффектов из авторского вида и тумблеров игрока.
/// </summary>
/// <remarks>
/// Отдельный тип, а не метод контроллера: контроллер занят Volume, камерами и
/// разделением слоёв, и сборка снимка к этому отношения не имеет. Заодно это
/// единственное место, где видно, какой тумблер за какие поля отвечает.
/// </remarks>
internal static class AdvancedPostProcessComposer
{
    public static AdvancedPostProcessSnapshot From(ClientConfig config)
    {
        // Масштабы и пороги берутся из вида всегда: они описывают форму
        // эффекта, а не его силу. Выключенный тумблер обнуляет только
        // интенсивность — так шейдер пропускает ветку целиком.
        return new AdvancedPostProcessSnapshot
        {
            LocalContrastIntensity = config.LocalContrastEnabled
                ? PostProcessLook.LocalContrast.Intensity
                : 0f,

            LensDirtIntensity = config.LensEffectsEnabled
                ? PostProcessLook.Lens.DirtIntensity
                : 0f,
            LensDirtScale = PostProcessLook.Lens.DirtScale,
            AnamorphicIntensity = config.LensEffectsEnabled
                ? PostProcessLook.Lens.AnamorphicIntensity
                : 0f,
            AnamorphicLength = PostProcessLook.Lens.AnamorphicLength,
            ChromaticDiffractionIntensity = config.LensEffectsEnabled
                ? PostProcessLook.Lens.DiffractionIntensity
                : 0f,
            GlintIntensity = config.LensEffectsEnabled
                ? PostProcessLook.Lens.GlintIntensity
                : 0f,
            GlintThreshold = PostProcessLook.Lens.GlintThreshold,

            VolumetricDustIntensity = config.AtmosphereEnabled
                ? PostProcessLook.Atmosphere.DustIntensity
                : 0f,
            VolumetricDustScale = PostProcessLook.Atmosphere.DustScale,
            VolumetricDustSpeed = PostProcessLook.Atmosphere.DustSpeed,
            HeatRefractionIntensity = config.AtmosphereEnabled
                ? PostProcessLook.Atmosphere.HeatRefractionIntensity
                : 0f,
            HeatRefractionScale = PostProcessLook.Atmosphere.HeatRefractionScale,

            PhosphorMaskIntensity = config.DisplayPhysicsEnabled
                ? PostProcessLook.Display.PhosphorMaskIntensity
                : 0f,
            DitheringIntensity = config.DisplayPhysicsEnabled
                ? PostProcessLook.Display.DitheringIntensity
                : 0f,

            TemporalPersistenceIntensity = config.TemporalEnabled
                ? PostProcessLook.Temporal.PersistenceIntensity
                : 0f,
            TemporalPersistenceDecay = PostProcessLook.Temporal.PersistenceDecay,
            LightStability = config.TemporalEnabled
                ? PostProcessLook.Temporal.LightStability
                : 0f,
        };
    }
}
