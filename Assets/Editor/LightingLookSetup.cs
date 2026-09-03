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

    /// <summary>
    /// Сила эмиссии. Три, а не восемь.
    /// </summary>
    /// <remarks>
    /// Восемь — верхняя граница собственного ползунка, а освещённая земля стоит
    /// 0.1..0.3 линейных: эмиссия шла до восьмидесяти раз ярче фона. Поканальная
    /// кривая AgX на таком входе сводит все три канала к верхушке, и ядра руды
    /// теряли цвет — насыщенность на экране 0.10 при 8 против 0.18 при 3.
    ///
    /// Три, а не два, потому что множитель работает дважды: на собственную
    /// яркость руды и на её вклад в марш лучей (`WorldLighting.compute:343`).
    /// Двойка срезала бы свет от руды вчетверо, и мир пришлось бы вытаскивать
    /// амбиентом, который и так стоит на 0.85 из максимума 1.
    /// </remarks>
    private const float EmissionScale = 3f;

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
            float previous = emission.floatValue;
            emission.floatValue = EmissionScale;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(defaults);
            AssetDatabase.SaveAssets();

            Debug.Log(
                $"[LightingLookSetup] _emissionScale: {previous} -> {EmissionScale}. " +
                "Перезайди в игру, чтобы значение доехало до ClientConfig.");
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
