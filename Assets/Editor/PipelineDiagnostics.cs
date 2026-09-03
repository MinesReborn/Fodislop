#if UNITY_EDITOR
#nullable enable

using System;
using UnityEditor;
using UnityEngine;

namespace Fodinae.EditorTools;

/// <summary>
/// Грубая бисекция конвейера: выключить всё необязательное и посмотреть, что
/// останется.
/// </summary>
/// <remarks>
/// ЗАЧЕМ. Когда дефект вида не удаётся локализовать рассуждением, дешевле
/// свести конвейер к минимуму, в котором кадр обязан быть верным, и добавлять
/// обратно по одному. Минимум здесь такой:
///
///   альбедо x (амбиент + прямой свет от эмиссии) -> AgX -> экран
///
/// Выключается всё, что складывается поверх: блум, оптика, атмосфера,
/// локальный контраст, временное накопление, зерно, виньетка, хроматика,
/// смаз, физика дисплея. Из света снимаются отскок и контактное затенение —
/// оба множат и добавляют, оба могут быть источником.
///
/// Тонмап не выключается: без него всё ярче белой точки срезается в плоский
/// белый, то есть кадр становится не проще, а неверен.
///
/// Если пересвет остаётся ПОСЛЕ этого — причина в минимальной цепочке, и
/// искать надо там: альбедо, эмиссия, амбиент, оценщик. Если исчезает —
/// причина в том, что выключили, и возвращать надо по одному.
///
/// Обратно: Fodinae/Rendering/Diagnostics/Restore Optional Pipeline.
/// </remarks>
internal static class PipelineDiagnostics
{
    private const string ProjectDefaultsPath =
        "Assets/Resources/Configuration/ProjectDefaults.asset";
    private const string DisablePath = "Fodinae/Rendering/Diagnostics/Disable Optional Pipeline";
    private const string RestorePath = "Fodinae/Rendering/Diagnostics/Restore Optional Pipeline";

    /// <summary>
    /// Поля-тумблеры и их авторские значения. Порядок и имена — те же, что в
    /// <c>ProjectDefaults</c>; при переименовании поля инструмент упадёт с
    /// понятной ошибкой, а не запишет ноль правок молча.
    /// </summary>
    private static readonly (string Field, bool Authored)[] OptionalToggles =
    [
        ("_bloomEnabled", true),
        ("_vignetteEnabled", true),
        ("_chromaticAberrationEnabled", false),
        ("_filmGrainEnabled", true),
        ("_motionBlurEnabled", false),
        ("_localContrastEnabled", true),
        ("_lensEffectsEnabled", true),
        ("_atmosphereEnabled", true),
        ("_displayPhysicsEnabled", false),
        ("_temporalEnabled", true),
        ("_ambientOcclusionEnabled", true),
        ("_diffuseBounceEnabled", true),
    ];

    [MenuItem(DisablePath)]
    public static void Disable()
    {
        Apply(enabled: false);
    }

    [MenuItem(RestorePath)]
    public static void Restore()
    {
        Apply(enabled: true);
    }

    private static void Apply(bool enabled)
    {
        try
        {
            var defaults = AssetDatabase.LoadAssetAtPath<ScriptableObject>(ProjectDefaultsPath);
            if (defaults == null)
            {
                throw new InvalidOperationException(
                    $"ProjectDefaults not found at {ProjectDefaultsPath}.");
            }

            var serialized = new SerializedObject(defaults);
            foreach ((string field, bool authored) in OptionalToggles)
            {
                FindProperty(serialized, field).boolValue = enabled && authored;
            }

            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(defaults);
            AssetDatabase.SaveAssets();

            Debug.Log(
                enabled
                    ? "[PipelineDiagnostics] Необязательные стадии возвращены к авторским значениям. " +
                      "Перезайди в игру."
                    : "[PipelineDiagnostics] Необязательные стадии выключены. Осталось: " +
                      "альбедо x (амбиент + прямой свет) -> AgX -> экран. Перезайди в игру.");
        }
        catch (Exception exception)
        {
            Debug.LogError($"[PipelineDiagnostics] Failed: {exception}");
            throw;
        }
    }

    private static SerializedProperty FindProperty(SerializedObject serialized, string name)
    {
        SerializedProperty iterator = serialized.GetIterator();
        while (iterator.NextVisible(true))
        {
            if (iterator.name == name)
            {
                return iterator.Copy();
            }
        }

        throw new InvalidOperationException(
            $"Serialized field '{name}' not found in {ProjectDefaultsPath}.");
    }
}
#endif
