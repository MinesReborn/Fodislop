#if UNITY_EDITOR
#nullable enable

using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Fodinae.EditorTools;

/// <summary>
/// One-way project setup for URP HDR/SDR display switching.
/// Scene rendering remains scene-linear HDR in both modes. Fodinae owns tone
/// mapping; URP only performs the final display encoding.
///
/// Run from Fodinae/Rendering/Apply HDR-SDR Dual Mode Setup.
/// </summary>
internal static class HdrSdrDualModeSetup
{
    private const string UniversalRPPath = "Assets/Settings/UniversalRP.asset";
    private const string VolumeProfilePath = "Assets/Settings/PostProcessVolumeProfile.asset";
    private const string MenuPath = "Fodinae/Rendering/Apply HDR-SDR Dual Mode Setup";

    [MenuItem(MenuPath)]
    public static void Apply()
    {
        try
        {
            ApplyPlayerSettings();
            ApplyUniversalRP();
            RemoveBuiltInTonemapping();
            AssetDatabase.SaveAssets();
            Debug.Log(
                "[HdrSdrDualModeSetup] HDR/SDR dual mode configured: " +
                "HDR resources included and Unity tonemapping removed from the custom profile.");
        }
        catch (Exception exception)
        {
            Debug.LogError($"[HdrSdrDualModeSetup] Failed: {exception}");
            throw;
        }
    }

    [MenuItem(MenuPath, true)]
    private static bool ValidateApply() => !Application.isPlaying;

    [InitializeOnLoadMethod]
    private static void AutoEnforcePlayerSettings()
    {
        if (PlayerSettings.useHDRDisplay)
        {
            PlayerSettings.useHDRDisplay = false;
        }

        if (!PlayerSettings.allowHDRDisplaySupport)
        {
            PlayerSettings.allowHDRDisplaySupport = true;
        }
    }

    private static void ApplyPlayerSettings()
    {
        // Include URP's HDR encoding resources even though the application
        // starts in SDR and opts into HDR later through HDROutputSettings.
        if (!PlayerSettings.allowHDRDisplaySupport)
        {
            PlayerSettings.allowHDRDisplaySupport = true;
            Debug.Log("[HdrSdrDualModeSetup] Enabled PlayerSettings.allowHDRDisplaySupport.");
        }

        // ApplicationBootstrap applies the saved preference before Gateway,
        // so the build default must not override a player who selected SDR.
        PlayerSettings.useHDRDisplay = false;
    }

    private static void ApplyUniversalRP()
    {
        var urp = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(UniversalRPPath);
        if (urp == null)
        {
            throw new InvalidOperationException(
                $"Required URP asset was not found at '{UniversalRPPath}'.");
        }

        var serialized = new SerializedObject(urp);
        SerializedProperty supportsHdr = serialized.FindProperty("m_SupportsHDR") ??
            throw new InvalidOperationException(
                $"URP asset '{UniversalRPPath}' does not expose m_SupportsHDR.");
        if (supportsHdr.boolValue)
        {
            return;
        }

        supportsHdr.boolValue = true;
        serialized.ApplyModifiedProperties();
        EditorUtility.SetDirty(urp);
        Debug.Log("[HdrSdrDualModeSetup] Enabled URP HDR render targets.");
    }

    private static readonly string[] CleanProfilePaths =
    [
        "Assets/Settings/PostProcessVolumeProfile.asset",
        "Assets/Settings/DefaultVolumeProfile.asset",
    ];

    private static readonly Type[] BuiltInDuplicateTypes =
    [
        typeof(Tonemapping),
        typeof(LiftGammaGain),
        typeof(ColorAdjustments),
        typeof(ColorCurves),
        typeof(Bloom),
        typeof(Vignette),
        typeof(ChromaticAberration),
        typeof(MotionBlur),
        typeof(FilmGrain),
        typeof(ChannelMixer),
        typeof(SplitToning),
        typeof(WhiteBalance),
        typeof(LensDistortion),
        typeof(PaniniProjection),
        typeof(DepthOfField),
    ];

    private static void RemoveBuiltInTonemapping()
    {
        var builtInTypesSet = new HashSet<Type>(BuiltInDuplicateTypes);
        foreach (string profilePath in CleanProfilePaths)
        {
            VolumeProfile? profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(profilePath);
            if (profile == null)
            {
                continue;
            }

            bool changed = profile.components.RemoveAll(component => component == null) > 0;
            var seenTypes = new HashSet<Type>();
            for (int i = profile.components.Count - 1; i >= 0; i--)
            {
                VolumeComponent component = profile.components[i];
                if (component == null)
                {
                    profile.components.RemoveAt(i);
                    changed = true;
                    continue;
                }

                Type type = component.GetType();
                if (builtInTypesSet.Contains(type) || !seenTypes.Add(type))
                {
                    profile.components.RemoveAt(i);
                    UnityEngine.Object.DestroyImmediate(component, allowDestroyingAssets: true);
                    changed = true;
                }
            }

            if (changed)
            {
                EditorUtility.SetDirty(profile);
                Debug.Log(
                    $"[HdrSdrDualModeSetup] Cleaned duplicate and built-in components from '{profilePath}'.");
            }
        }
    }
}
#endif
