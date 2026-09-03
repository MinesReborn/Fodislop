#if UNITY_EDITOR
#nullable enable

using System;
using UnityEditor;
using UnityEngine;

namespace Fodinae.EditorTools;

/// <summary>
/// Записывает авторские величины освещения в <c>ProjectDefaults.asset</c>.
/// </summary>
/// <remarks>
/// ЗАЧЕМ. Величины света сериализованы в ассете, а `.asset` нельзя править
/// текстом (AGENTS.md §62): YAML держит GUID-ы и порядок полей, и ручная
/// правка ломает их молча. Единственный разрешённый путь — Editor API, и этот
/// инструмент им и является. Значения ниже — решение, а не подстройка, поэтому
/// они записаны здесь, где их видно и в code review, а не только в инспекторе.
///
/// Запуск: Fodinae/Rendering/Apply Authored Lighting Look.
/// </remarks>
internal static class LightingLookSetup
{
    private const string ProjectDefaultsPath =
        "Assets/Resources/Configuration/ProjectDefaults.asset";
    private const string MenuPath = "Fodinae/Rendering/Apply Authored Lighting Look";

    // ЗНАЧЕНИЯ НИЖЕ — АВТОРСКИЕ, вернувшиеся на место.
    //
    // 03.09.2026 я подбирал их под скриншоты, гоняясь за пересветом: эмиссию
    // опускал до 3 и до 6, амбиент задирал в восемь раз, включал потолок света
    // и опускал его до единицы. Всё это компенсировало один дефект, а не
    // настраивало вид: оценщик освещённости делил на ЧИСЛО направлений вместо
    // суммы весов, и поверхность с нормалью недополучала ровно в пи раз
    // (0.318 L вместо L), причём её яркость зависела от угла поворота стены.
    // Чтобы такие поверхности было видно, силу эмиссии приходилось задирать —
    // и во столько же раз выбивало всё, у чего нормали нет. Пересвет и
    // плоскость были одним и тем же дефектом, наблюдаемым с двух сторон.
    //
    // Дефект исправлен в WorldLighting.compute (ResolveDirect и
    // SolveDiffuseBounce делят на сумму весов). Компенсации сняты: на верной
    // математике они больше ничего не компенсируют, а только врут о замысле.
    // Если после этого вид не устроит — это уже решение о виде, и менять его
    // надо здесь, осознанно, а не подгонкой под кадр.

    /// <summary>Сила эмиссии. Авторское значение.</summary>
    private const float EmissionScale = 8f;

    /// <summary>
    /// Потолок освещения. Выключен — таким он и был задуман.
    /// </summary>
    /// <remarks>
    /// Кламп ничего не исправляет, он срезает вершину. Пока причина
    /// завышенного света была в оценщике, включённый потолок прятал её и
    /// заодно плющил кадр. Оставлен выключенным: если свет снова уйдёт за
    /// разумные пределы, искать надо причину, а не включать потолок.
    /// </remarks>
    private const bool EnableFinalLightingClamp = false;

    /// <summary>Верх освещения, если потолок всё же включат.</summary>
    private const float MaximumLightMultiplier = 1f;

    /// <summary>Цвет и сила фонового света. Авторское значение.</summary>
    private static readonly Color AmbientColor = new(0.12f, 0.14f, 0.18f, 1f);

    [MenuItem(MenuPath)]
    public static void Apply()
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

            SerializedProperty emission = FindProperty(serialized, "_emissionScale");
            float previousEmission = emission.floatValue;
            emission.floatValue = EmissionScale;

            SerializedProperty clamp = FindProperty(serialized, "_enableFinalLightingClamp");
            bool previousClamp = clamp.boolValue;
            clamp.boolValue = EnableFinalLightingClamp;

            SerializedProperty maximum = FindProperty(serialized, "_maximumLightMultiplier");
            float previousMaximum = maximum.floatValue;
            maximum.floatValue = MaximumLightMultiplier;

            SerializedProperty ambient = FindProperty(serialized, "_ambientColor");
            Color previousAmbient = ambient.colorValue;
            ambient.colorValue = AmbientColor;

            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(defaults);
            AssetDatabase.SaveAssets();

            Debug.Log(
                $"[LightingLookSetup] _emissionScale: {previousEmission} -> {EmissionScale}; " +
                $"_enableFinalLightingClamp: {previousClamp} -> {EnableFinalLightingClamp}; " +
                $"_maximumLightMultiplier: {previousMaximum} -> {MaximumLightMultiplier}; " +
                $"_ambientColor: {previousAmbient} -> {AmbientColor}. " +
                "Перезайди в игру, чтобы значения доехали до ClientConfig.");
        }
        catch (Exception exception)
        {
            Debug.LogError($"[LightingLookSetup] Failed: {exception}");
            throw;
        }
    }

    private static SerializedProperty FindProperty(SerializedObject serialized, string name)
    {
        // FindProperty ищет только по верхнему уровню, а поля света лежат во
        // вложенном сериализованном блоке. Обходим дерево целиком, иначе
        // инструмент молча не найдёт поле и запишет ноль правок.
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
