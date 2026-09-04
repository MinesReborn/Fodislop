#nullable enable

using System;

namespace Fodinae.Core;

/// <summary>
/// Тумблеры постпроцесса: включён эффект или нет.
/// </summary>
/// <remarks>
/// Промежуточных значений здесь нет намеренно. Величины — сила блума, радиус
/// виньетки, зерно — авторские и живут в
/// <c>Fodinae.Rendering.PostProcessing.PostProcessLook</c>. Игроку задаётся
/// вопрос «нужен ли эффект», а не «сколько его»: второе — вопрос
/// художественный, и разброс по нему и был тем, что делало вид кадра
/// не решением, а случайностью.
/// </remarks>
[Serializable]
public sealed class EffectSettings
{
    [SettingUnbounded("Тумблер свечения ярких участков.")]
    [SettingLabel("settings.effects.bloom")]
    [SettingConsumer(SettingConsumerTarget.PostProcessController, "PostProcessController.BloomIntensity")]
    public bool BloomEnabled = true;

    [SettingUnbounded("Тумблер затемнения к краям кадра.")]
    [SettingLabel("settings.effects.vignette")]
    [SettingConsumer(SettingConsumerTarget.PostProcessController, "PostProcessController.VignetteIntensity")]
    public bool VignetteEnabled = true;

    [SettingUnbounded("Тумблер расхождения каналов.")]
    [SettingLabel("settings.effects.chromatic_aberration")]
    [SettingConsumer(SettingConsumerTarget.PostProcessController, "PostProcessController.ChromaticAberrationIntensity")]
    public bool ChromaticAberrationEnabled;

    [SettingUnbounded("Тумблер плёночного зерна.")]
    [SettingLabel("settings.effects.grain")]
    [SettingConsumer(SettingConsumerTarget.PostProcessController, "AdvancedPostProcessComposer -> FilmGrain")]
    public bool FilmGrainEnabled = true;

    [SettingUnbounded("Тумблер смаза движения.")]
    [SettingLabel("settings.effects.motion_blur")]
    [SettingConsumer(SettingConsumerTarget.PostProcessController, "PostProcessController.MotionBlurIntensity")]
    public bool MotionBlurEnabled;

    [SettingUnbounded("Тумблер локального контраста.")]
    [SettingLabel("settings.effects.local_sharpness")]
    [SettingConsumer(SettingConsumerTarget.PostProcessController, "AdvancedPostProcessComposer -> LocalContrast")]
    public bool LocalContrastEnabled = true;

    [SettingUnbounded("Тумблер оптических дефектов объектива.")]
    [SettingLabel("settings.effects.anamorphic_beams")]
    [SettingConsumer(SettingConsumerTarget.PostProcessController, "AdvancedPostProcessComposer -> AnamorphicLens")]
    public bool LensEffectsEnabled = true;

    [SettingUnbounded("Тумблер объёмной пыли и теплового искажения.")]
    [SettingLabel("settings.effects.glow_dust")]
    [SettingConsumer(SettingConsumerTarget.PostProcessController, "AdvancedPostProcessComposer -> Atmosphere")]
    public bool AtmosphereEnabled = true;

    [SettingUnbounded("Тумблер фосфорной маски и дизеринга.")]
    [SettingLabel("settings.effects.phosphor_pattern")]
    [SettingConsumer(SettingConsumerTarget.PostProcessController, "AdvancedPostProcessComposer -> PhosphorGrid")]
    public bool DisplayPhysicsEnabled;

    [SettingUnbounded("Тумблер временного накопления.")]
    [SettingLabel("settings.effects.phosphor_afterglow")]
    [SettingConsumer(SettingConsumerTarget.PostProcessController, "AdvancedPostProcessComposer -> TemporalAccumulation")]
    public bool TemporalEnabled = true;
}
