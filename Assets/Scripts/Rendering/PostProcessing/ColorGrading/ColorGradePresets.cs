#nullable enable

using System;
using System.IO;
using System.Text;
using UnityEngine;

namespace Kern.Rendering.PostProcessing;

internal static class ColorGradePresets
{
    public static string PresetDirectory =>
        Path.Combine(Application.persistentDataPath, "color_grade_presets");

    public static string GetPresetPath(string name) =>
        Path.Combine(PresetDirectory, SanitizePresetName(name) + ".json");

    public static string[] ListPresets()
    {
        if (!Directory.Exists(PresetDirectory))
        {
            return Array.Empty<string>();
        }

        string[] paths = Directory.GetFiles(PresetDirectory, "*.json");
        for (int index = 0; index < paths.Length; index++)
        {
            paths[index] = Path.GetFileNameWithoutExtension(paths[index]);
        }

        Array.Sort(paths, StringComparer.OrdinalIgnoreCase);
        return paths;
    }

    public static bool SavePreset(
        ColorGradeState state,
        ColorGradeZones? zones,
        string name)
    {
        string presetPath = GetPresetPath(name);
        if (!ColorGradeFile.Save(state, zones))
        {
            return false;
        }

        try
        {
            Directory.CreateDirectory(PresetDirectory);
            ColorGradeFile.WriteAtomically(presetPath, File.ReadAllText(ColorGradeFile.Path));
            Debug.Log($"[ColorGrade] Пресет сохранён -> {presetPath}");
            return true;
        }
        catch (Exception exception)
        {
            Debug.LogError($"[ColorGrade] Не удалось сохранить пресет {presetPath}: {exception.Message}");
            return false;
        }
    }

    public static bool TryLoadPreset(
        ColorGradeState state,
        ColorGradeZones? zones,
        string name)
    {
        string presetPath = GetPresetPath(name);
        if (!File.Exists(presetPath))
        {
            return false;
        }

        try
        {
            string json = File.ReadAllText(presetPath);
            if (!ColorGradeFile.IsValidPresetPayload(json))
            {
                Debug.LogWarning($"[ColorGrade] Пресет {presetPath} не разобран.");
                return false;
            }

            ColorGradeFile.WriteAtomically(ColorGradeFile.Path, json);
            return ColorGradeFile.TryLoad(state, zones);
        }
        catch (Exception exception)
        {
            Debug.LogError($"[ColorGrade] Не удалось загрузить пресет {presetPath}: {exception.Message}");
            return false;
        }
    }

    private static string SanitizePresetName(string name)
    {
        string value = string.IsNullOrWhiteSpace(name) ? "default" : name.Trim();
        char[] invalid = Path.GetInvalidFileNameChars();
        var builder = new StringBuilder(value.Length);
        foreach (char character in value)
        {
            bool forbidden = Array.IndexOf(invalid, character) >= 0 ||
                char.IsControl(character) ||
                character == '/' ||
                character == '\\';
            if (!forbidden)
            {
                builder.Append(character);
            }
        }

        string sanitized = builder.ToString().Trim().Trim('.');
        return string.IsNullOrWhiteSpace(sanitized) ? "default" : sanitized;
    }
}
