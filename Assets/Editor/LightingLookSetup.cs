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

    /// <summary>
    /// Потолок освещения. Включён.
    /// </summary>
    /// <remarks>
    /// Это единственный коэффициент, который делает все остальные
    /// ограниченными. Выключенным он оставлял мировому свету неограниченный
    /// верх: замер по скриншоту 03.09.2026 дал на здании множитель около 8.7
    /// при альбедо 0.92, то есть 8.0 линейных — вчетверо выше белой точки.
    /// Ни тонмап, ни блум, ни экспозиция такого не чинят: кадр, где свет
    /// уходит в восьмёрку, выбит по построению.
    ///
    /// Кламп сохраняет цветность (масштабирует RGB вместе, а не режет каналы
    /// поодиночке), поэтому потолок не красит света в белое, а держит их.
    /// </remarks>
    private const bool EnableFinalLightingClamp = true;

    /// <summary>
    /// Верх освещения. Двойка, а не единица.
    /// </summary>
    /// <remarks>
    /// Единица означает: яркость пикселя не может превысить его собственное
    /// АЛЬБЕДО. Самый белый пиксель проекта выходит на 199/255, абсолютный
    /// максимум кадра — 202/255, то есть клип становится невозможен
    /// арифметически, а не по настройке.
    ///
    /// Двойка, стоявшая здесь до этого, оставляла стоп запаса под свечение,
    /// но при насыщенном светом кадре потолок достигается почти везде, и
    /// картинка превращается в альбедо, умноженное на два: ровно то же
    /// плоское пересвеченное поле, только с другим числом.
    ///
    /// Свечение источников теперь берётся не превышением над единицей, а
    /// порогом блума ниже неё (см. PostProcessLook.Bloom.Threshold).
    /// </remarks>
    private const float MaximumLightMultiplier = 1f;

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

            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(defaults);
            AssetDatabase.SaveAssets();

            Debug.Log(
                $"[LightingLookSetup] _emissionScale: {previousEmission} -> {EmissionScale}; " +
                $"_enableFinalLightingClamp: {previousClamp} -> {EnableFinalLightingClamp}; " +
                $"_maximumLightMultiplier: {previousMaximum} -> {MaximumLightMultiplier}. " +
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
