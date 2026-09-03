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

    /// <summary>
    /// Сила эмиссии. Два, и это выведено, а не подобрано на глаз.
    /// </summary>
    /// <remarks>
    /// Восьмёрка была компенсацией дефекта оценщика: поверхность с нормалью
    /// недополучала ровно в пи раз, и силу задирали, чтобы стены было видно.
    /// Дефект исправлен, компенсация стала лишней и превратилась в свою
    /// противоположность — замер это подтвердил численно: множитель света на
    /// земле вырос с 2.6 до 8.8, отношение 3.38 против пи = 3.14.
    ///
    /// Величина выведена по критерию, а не по впечатлению: освещённая земля
    /// должна ложиться на средне-серый, то есть 0.18 линейных. Её альбедо
    /// 0.078, значит нужен множитель света около 2.2, что при измеренной
    /// зависимости даёт силу эмиссии 2. На экране это 125/255 против нынешних
    /// 187. Хочешь мир темнее или светлее — двигать надо ЭТО число, и оно
    /// теперь означает ровно то, что написано.
    /// </remarks>
    private const float EmissionScale = 2f;

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

    /// <summary>
    /// ДИАГНОСТИКА. Выключить все необязательные стадии конвейера.
    /// </summary>
    /// <remarks>
    /// Грубая бисекция: свести кадр к минимуму, в котором он обязан быть
    /// верным, и добавлять обратно по одному. Минимум такой:
    ///
    ///   альбедо x (амбиент + прямой свет от эмиссии) -> AgX -> экран
    ///
    /// Снимается всё, что складывается поверх: блум, оптика, атмосфера,
    /// локальный контраст, временное накопление, зерно, виньетка, хроматика,
    /// смаз, физика дисплея. Из света — отскок и контактное затенение.
    /// Тонмап не снимается: без него всё ярче белой точки срезается в плоский
    /// белый, то есть кадр становится не проще, а неверен.
    ///
    /// Останется пересвет — причина в минимальной цепочке: альбедо, эмиссия,
    /// амбиент, оценщик. Исчезнет — причина в снятом, возвращать по одному.
    ///
    /// ВЕРНУТЬ: поставить false и запустить пункт меню ещё раз.
    /// </remarks>
    private const bool DisableOptionalStages = true;

    private static readonly (string Field, bool Authored)[] OptionalStages =
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

            foreach ((string field, bool authored) in OptionalStages)
            {
                FindProperty(serialized, field).boolValue =
                    !DisableOptionalStages && authored;
            }

            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(defaults);
            AssetDatabase.SaveAssets();

            Debug.Log(
                $"[LightingLookSetup] _emissionScale: {previousEmission} -> {EmissionScale}; " +
                $"_enableFinalLightingClamp: {previousClamp} -> {EnableFinalLightingClamp}; " +
                $"_maximumLightMultiplier: {previousMaximum} -> {MaximumLightMultiplier}; " +
                $"_ambientColor: {previousAmbient} -> {AmbientColor}. " +
                (DisableOptionalStages
                    ? "Необязательные стадии ВЫКЛЮЧЕНЫ (диагностика). "
                    : "Необязательные стадии по авторским значениям. ") +
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
