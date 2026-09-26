#nullable enable

using System;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using UnityEngine;
using UnityEngine.Rendering;

namespace Kern.Rendering.PostProcessing
{

public static class PostProcessVolumeUtilities
{
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
}
