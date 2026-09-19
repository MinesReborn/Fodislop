#nullable enable

using System;
using Kern.Rendering.PostProcessing;

namespace Kern.Core;

[Serializable]
public sealed class EffectSettings
{
    // Дефолти — авторський вигляд, єдиний дім у PostProcessLook.Effects.
    [SettingUnbounded("Тумблер свечения ярких участков.")]
    [SettingLabel("settings.effects.bloom")]
    [SettingConsumer(SettingConsumerTarget.PostProcessController, "PostProcessController.BloomIntensity")]
    public bool BloomEnabled = PostProcessLook.Effects.Bloom;

    [SettingUnbounded("Тумблер затемнения к краям кадра.")]
    [SettingLabel("settings.effects.vignette")]
    [SettingConsumer(SettingConsumerTarget.PostProcessController, "PostProcessController.VignetteIntensity")]
    public bool VignetteEnabled = PostProcessLook.Effects.Vignette;

    // Раньше поле называлось FilmGrainEnabled и подписывалось «зерном», хотя
    // включало эйгенграу целиком: зерно рисуется внутри его ветки шейдера.
    [SettingUnbounded("Тумблер эйгенграу — шума и подсветки в тёмных участках.")]
    [SettingLabel("settings.effects.eigengrau")]
    [SettingConsumer(SettingConsumerTarget.PostProcessController, "PostProcessController.EigengrauIntensity")]
    public bool EigengrauEnabled = PostProcessLook.Effects.Eigengrau;

    [SettingUnbounded("Тумблер смаза движения.")]
    [SettingLabel("settings.effects.motion_blur")]
    [SettingConsumer(SettingConsumerTarget.PostProcessController, "PostProcessController.MotionBlurIntensity")]
    public bool MotionBlurEnabled = PostProcessLook.Effects.MotionBlur;
}
