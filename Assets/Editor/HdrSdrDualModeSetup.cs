#if UNITY_EDITOR
#nullable enable

using System;
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

    private static void RemoveBuiltInTonemapping()
    {
        VolumeProfile profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(VolumeProfilePath) ??
            throw new InvalidOperationException(
                $"Required VolumeProfile was not found at '{VolumeProfilePath}'.");

        bool changed = profile.components.RemoveAll(component => component == null) > 0;
        if (profile.TryGet(out UnityEngine.Rendering.Universal.Tonemapping? tonemapping) &&
            tonemapping != null)
        {
            profile.Remove<UnityEngine.Rendering.Universal.Tonemapping>();
            UnityEngine.Object.DestroyImmediate(tonemapping, allowDestroyingAssets: true);
            changed = true;
        }

        if (!changed)
        {
            return;
        }

        EditorUtility.SetDirty(profile);
        Debug.Log(
            $"[HdrSdrDualModeSetup] Removed Unity Tonemapping and stale entries from " +
            $"'{VolumeProfilePath}'.");
    }
}
#endif
