#nullable enable

using System;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using UnityEngine;
using UnityEngine.Rendering;

namespace Kern.Rendering.PostProcessing;

public static class PostProcessDefaults
{
    // These values only construct valid VolumeParameter instances for Unity
    // serialization. ProjectDefaults/ClientConfig is the sole visual source
    // of truth and overwrites every parameter before the first render.
    public static ClampedFloatParameter BloomIntensity() => new(
        PostProcessLimits.BloomIntensityMin,
        PostProcessLimits.BloomIntensityMin,
        PostProcessLimits.BloomIntensityMax);

    // Порог теперь относительный: во сколько раз пиксель обязан превзойти
    // собственный локальный фон. Значения ниже единицы лишены смысла — они
    // зажгли бы сам фон, — поэтому нижняя граница равна единице, а не нулю.
    public static ClampedFloatParameter BloomThreshold() => new(1.6f, 1f, 8f);

    public static ClampedFloatParameter BloomSoftKnee() => new(0.5f, 0f, 1f);

    public static ClampedFloatParameter BloomRadius() => new(3f, 0.5f, 8f);

    public static ClampedFloatParameter BloomScatter() => new(0.1f, 0.1f, 1f);

    public static ColorParameter BloomTint() => new(Color.white);

    public static ClampedFloatParameter VignetteIntensity() => new(0f, 0f, 1f);

    public static ColorParameter VignetteColor() => new(Color.black);

    public static ClampedFloatParameter VignetteSmoothness() => new(0.01f, 0.01f, 1f);

    public static Vector2Parameter VignetteCenter() => new(new Vector2(0.5f, 0.5f));

    public static ClampedFloatParameter ColorGradingExposure() => new(
        0f,
        PostProcessLimits.ExposureMin,
        PostProcessLimits.ExposureMax);

    public static ColorParameter ColorGradingFilter() => new(Color.white);

    public static ClampedFloatParameter ColorGradingContrast() => new(
        0f,
        PostProcessLimits.ContrastMin,
        PostProcessLimits.ContrastMax);

    public static ClampedFloatParameter ColorGradingSaturation() => new(1f, 0f, 2f);

    public static ClampedFloatParameter EigengrauIntensity() => new(
        PostProcessLimits.EigengrauIntensityMin,
        PostProcessLimits.EigengrauIntensityMin,
        PostProcessLimits.EigengrauIntensityMax);

    public static ColorParameter EigengrauColor() => new(Color.black);

    public static ClampedFloatParameter EigengrauDarknessThreshold() => new(0.02f, 0.02f, 0.75f);

    public static ClampedFloatParameter EigengrauNoiseScale() => new(0.75f, 0.75f, 2f);

    public static ClampedFloatParameter EigengrauAnimationSpeed() => new(1f, 1f, 60f);

    public static ClampedFloatParameter MotionBlurIntensity() => new(
        PostProcessLimits.MotionBlurIntensityMin,
        PostProcessLimits.MotionBlurIntensityMin,
        PostProcessLimits.MotionBlurIntensityMax);

    public static void RequireVolumeComponent<T>(
        [NotNull] ref T? target,
        VolumeProfile profile)
        where T : VolumeComponent
    {
        if (!profile.TryGet(out target) || target == null)
        {
            target = profile.Add<T>(overrides: true);
            if (target == null)
            {
                throw new InvalidOperationException(
                    $"Post-process VolumeProfile '{profile.name}' is missing " +
                    $"the required '{typeof(T).Name}' component and could not create it.");
            }
        }

        EnableOverrides(target);
    }

    public static void ValidateVolumeProfile(VolumeProfile profile)
    {
        int removed = profile.components.RemoveAll(component => component == null);
        if (removed > 0)
        {
            Debug.LogWarning(
                $"[PostProcessController] Cleaned up {removed} null/missing component(s) " +
                $"from VolumeProfile '{profile.name}'.");
        }
    }

    private static void EnableOverrides(VolumeComponent component)
    {
        FieldInfo[] fields = component.GetType().GetFields(
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        foreach (FieldInfo field in fields)
        {
            if (!typeof(VolumeParameter).IsAssignableFrom(field.FieldType))
            {
                continue;
            }

            object? value = field.GetValue(component);
            if (value is not VolumeParameter parameter)
            {
                throw new InvalidOperationException(
                    $"Post-process component '{component.GetType().FullName}' has a null " +
                    $"parameter field '{field.Name}'.");
            }

            parameter.overrideState = true;
        }
    }
}
