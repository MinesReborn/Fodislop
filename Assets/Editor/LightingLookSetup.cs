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
    /// Шесть, а не три: цвет тайла перестал завышаться (см. Terrain.shader,
    /// перевод sRGB -> linear), и та же руда стала отдавать примерно вдвое
    /// меньше энергии. Множитель компенсирует ровно это — свечение возвращено
    /// к прежней относительной силе, но теперь считается от честного цвета.
    /// </remarks>
    private const float EmissionScale = 6f;

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
    /// Верх освещения. Страховка, а не регулятор.
    /// </summary>
    /// <remarks>
    /// Кламп ничего не настраивает — он только не даёт кадру выбиться, если
    /// где-то вылезет неограниченная величина. Настоящая причина завышенного
    /// света была в другом: цвет тайла уходил в решатель как sRGB-код, взятый
    /// за линейную энергию, и тёмные тайлы завышались в пятнадцать раз
    /// (см. Terrain.shader, LightingMaterialField). После линеаризации свет
    /// лежит в своём диапазоне сам, и потолок почти нигде не достигается.
    ///
    /// Поэтому здесь двойка, а не единица: единица прижимала кадр под белую
    /// точку и делала работу, которую должен делать сам решатель — то есть
    /// лечила симптом. Стоп запаса оставлен под свечение источников.
    /// </remarks>
    private const float MaximumLightMultiplier = 2f;

    /// <summary>
    /// Цвет и сила фонового света.
    /// </summary>
    /// <remarks>
    /// Прежние 0.12/0.14/0.18 давали в сумме около 0.13 — калибровка под
    /// завышенное альбедо, где тёмный тайл был в пятнадцать раз ярче
    /// положенного. После перевода в линейное та же величина оставляла землю
    /// на 21/255: мир проваливался в черноту всюду, куда не дотягивался
    /// источник. Замер по скриншоту 03.09.2026 совпал с расчётом (18 против
    /// 21), поэтому величина подобрана по той же модели: 0.89 выводит землю
    /// с альбедо 0.078 на 84/255 — читаемый тёмный пол шахты.
    ///
    /// Оттенок оставлен холодным, но заметно менее насыщенным: прежнее
    /// отношение 0.12:0.14:0.18 при такой силе окрасило бы весь мир в синий,
    /// тогда как на прежней яркости оно читалось лишь как лёгкий холод.
    /// </remarks>
    private static readonly Color AmbientColor = new(1.0f, 1.05f, 1.2f, 1f);

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
