#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using Fodinae.Audio.Core;
using Fodinae.Core;
using Fodinae.Rendering;
using Fodinae.World.Lighting.Quality;
using UnityEngine;

namespace Fodinae.SettingsProbe;

/// <summary>
/// Исполняет логику настроек вне Unity и печатает найденные расхождения.
/// </summary>
/// <remarks>
/// Проверки здесь — не про «компилируется», а про «работает на настоящих
/// данных». Каждая соответствует уже случившейся поломке либо той, которую
/// нечем было бы поймать иначе.
/// </remarks>
internal static class Program
{
    private static readonly List<string> Failures = [];
    private static int _checks;

    private static int Main(string[] args)
    {
        string root = ResolveRepositoryRoot(args);

        CheckEverySectionValidatesItsOwnDefaults();
        CheckDefaultsAreReportedAsDefaults();
        CheckClampProducesValidValues();
        CheckRangesAreOrderedAndFinite();
        CheckAudioBusRegistryCoversEveryBus();
        CheckEnumLabelsResolve();
        CheckDeclaredLabelsExistInLocalization(root);
        CheckGraphicsQualityRangesAreDeclared();
        CheckEveryFieldDeclaresConsumer();
        CheckLightingConfigHolderDirtyTracking();
        CheckSettingMutationDetection();
        CheckGraphicsQualitySettingsMutationAndMsaaCycle();
        CheckToneMappingWhitePointAndDisplayCalibration();
        CheckPostProcessLookAllConstantsValidViaReflection();
        CheckConsumerTargetsAndMechanismsReflection(root);

        Console.WriteLine();
        if (Failures.Count == 0)
        {
            Console.WriteLine($"PASSED: {_checks} проверок настроек, расхождений нет.");
            return 0;
        }

        Console.WriteLine($"FAILED: {Failures.Count} расхождений из {_checks} проверок.");
        foreach (string failure in Failures)
        {
            Console.WriteLine("  " + failure);
        }

        return 1;
    }

    /// <summary>
    /// Каждая секция обязана пройти собственную валидацию в исходном виде.
    /// </summary>
    /// <remarks>
    /// Ровно эта проверка поймала бы падение запуска: у DisplaySettings поля
    /// ResolutionWidth и ResolutionHeight — целые без диапазона, они не
    /// совпадали ни с одной веткой разбора и проваливались в default с
    /// исключением. Компилятор такое не видит: ветка `case int when ...`
    /// синтаксически безупречна.
    /// </remarks>
    private static void CheckEverySectionValidatesItsOwnDefaults()
    {
        Section("значения по умолчанию проходят собственную валидацию");
        ForEachSection((name, validate, _, _) =>
        {
            _checks++;
            try
            {
                validate();
            }
            catch (Exception ex)
            {
                Failures.Add($"{name}: значения по умолчанию не проходят валидацию — {ex.Message}");
            }
        });
    }

    /// <summary>
    /// Свежая секция обязана опознаваться как «не тронута игроком».
    /// </summary>
    /// <remarks>
    /// На этом держится инвариант «стандартный пресет графики не изменён»:
    /// если MatchesDefaults врёт на пустой секции, валидатор откажется
    /// загружать конфиг с любым стандартным пресетом.
    /// </remarks>
    private static void CheckDefaultsAreReportedAsDefaults()
    {
        Section("свежая секция опознаётся как авторская");
        ForEachSection((name, _, matchesDefaults, _) =>
        {
            _checks++;
            if (!matchesDefaults())
            {
                Failures.Add($"{name}: новый экземпляр не совпадает сам с собой в MatchesDefaults");
            }
        });
    }

    /// <summary>
    /// После клампинга секция обязана проходить валидацию.
    /// </summary>
    /// <remarks>
    /// Клампинг применяется при миграции старого файла. Если он оставляет
    /// значение вне границ, валидатор сразу за ним отказывается загружать
    /// конфиг — то есть игрок с давним сохранением не запустит игру вовсе.
    /// Проба загоняет в каждое числовое поле заведомо запредельное значение и
    /// проверяет, что связка «кламп, затем валидация» это переживает.
    /// </remarks>
    private static void CheckClampProducesValidValues()
    {
        Section("кламп приводит запредельные значения к допустимым");
        ForEachSection((name, _, _, clampExtreme) =>
        {
            _checks++;
            try
            {
                clampExtreme();
            }
            catch (Exception ex)
            {
                Failures.Add($"{name}: после клампа валидация всё ещё падает — {ex.Message}");
            }
        });
    }

    /// <summary>
    /// Границы обязаны быть конечными и не перевёрнутыми.
    /// </summary>
    private static void CheckRangesAreOrderedAndFinite()
    {
        Section("объявленные границы осмысленны");
        foreach (Type type in SectionTypes())
        {
            foreach (FieldInfo field in type.GetFields(BindingFlags.Instance | BindingFlags.Public))
            {
                SettingRangeAttribute? range = field.GetCustomAttribute<SettingRangeAttribute>();
                bool unbounded = field.GetCustomAttribute<SettingUnboundedAttribute>() != null;
                _checks++;
                if (range == null && !unbounded)
                {
                    Failures.Add($"{type.Name}.{field.Name}: нет ни [SettingRange], ни [SettingUnbounded]");
                    continue;
                }

                if (range == null)
                {
                    continue;
                }

                if (unbounded)
                {
                    Failures.Add($"{type.Name}.{field.Name}: одновременно [SettingRange] и [SettingUnbounded]");
                }

                if (float.IsNaN(range.Minimum) || float.IsInfinity(range.Minimum) ||
                    float.IsNaN(range.Maximum) || float.IsInfinity(range.Maximum))
                {
                    Failures.Add($"{type.Name}.{field.Name}: границы не конечны [{range.Minimum}, {range.Maximum}]");
                }
                else if (range.Minimum >= range.Maximum)
                {
                    Failures.Add(
                        $"{type.Name}.{field.Name}: пустой диапазон [{range.Minimum}, {range.Maximum}] — " +
                        "ползунок не сдвинется");
                }
            }
        }
    }

    /// <summary>
    /// Каждая шина обязана иметь и путь FMOD, и поле громкости.
    /// </summary>
    /// <remarks>
    /// Реестр бросает исключение сам, но только при первом обращении — то есть
    /// на инициализации звука у игрока. Проба вызывает его заранее.
    /// </remarks>
    private static void CheckAudioBusRegistryCoversEveryBus()
    {
        Section("каждая аудио-шина связана с путём и с полем громкости");
        _checks++;
        try
        {
            IReadOnlyList<AudioBusRegistry.BusBinding> buses = AudioBusRegistry.Buses;
            var covered = buses.Select(binding => binding.Bus).ToHashSet();
            foreach (AudioBusType bus in Enum.GetValues<AudioBusType>())
            {
                _checks++;
                if (!covered.Contains(bus))
                {
                    Failures.Add($"AudioBusType.{bus}: нет связки в AudioBusRegistry");
                }
            }

            var audio = new AudioSettings();
            foreach (AudioBusRegistry.BusBinding binding in buses)
            {
                _checks++;
                binding.Write(audio, 0.5f);
                if (Math.Abs(binding.Read(audio) - 0.5f) > 1e-6f)
                {
                    Failures.Add($"AudioBusType.{binding.Bus}: запись громкости не читается обратно");
                }
            }
        }
        catch (Exception ex)
        {
            Failures.Add($"AudioBusRegistry не строится — {ex.Message}");
        }
    }

    /// <summary>
    /// Каждое значение перечисления, показываемое игроку, обязано иметь подпись.
    /// </summary>
    /// <remarks>
    /// Раньше подписи лежали массивом, индексируемым значением перечисления, и
    /// новое значение давало чужую подпись либо выход за границы массива при
    /// открытии настроек.
    /// </remarks>
    private static void CheckEnumLabelsResolve()
    {
        Section("у показываемых значений перечислений есть подписи");
        foreach (GraphicsPreset preset in Enum.GetValues<GraphicsPreset>())
        {
            _checks++;
            try
            {
                _ = SettingSchema.LabelOf(preset);
            }
            catch (Exception ex)
            {
                Failures.Add($"GraphicsPreset.{preset}: {ex.Message}");
            }
        }

        foreach (LightingQualityMode mode in Enum.GetValues<LightingQualityMode>())
        {
            _checks++;
            try
            {
                _ = SettingSchema.LabelOf(mode);
            }
            catch (Exception ex)
            {
                Failures.Add($"LightingQualityMode.{mode}: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Каждый объявленный ключ подписи обязан существовать в локализации.
    /// </summary>
    /// <remarks>
    /// Отсутствующий ключ не роняет ничего: игрок просто видит в меню
    /// «settings.advanced.ao_radius» вместо названия. Это та же тихая смерть,
    /// только на экране.
    /// </remarks>
    private static void CheckDeclaredLabelsExistInLocalization(string root)
    {
        Section("ключи подписей существуют в локализации");
        string ruPath = Path.Combine(root, "Assets/Resources/Localization/ru.json");
        if (!File.Exists(ruPath))
        {
            Failures.Add($"не найден файл локализации {ruPath}");
            return;
        }

        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(ruPath));
        var keys = document.RootElement.EnumerateObject().Select(property => property.Name).ToHashSet();

        foreach (string key in DeclaredLabelKeys())
        {
            _checks++;
            if (!keys.Contains(key))
            {
                Failures.Add($"ключ подписи '{key}' объявлен в [SettingLabel], но его нет в ru.json");
            }
        }
    }

    private static IEnumerable<string> DeclaredLabelKeys()
    {
        var types = SectionTypes()
            .Concat([typeof(GraphicsPreset), typeof(LightingQualityMode)]);
        foreach (Type type in types)
        {
            BindingFlags flags = type.IsEnum
                ? BindingFlags.Static | BindingFlags.Public
                : BindingFlags.Instance | BindingFlags.Public;
            foreach (FieldInfo field in type.GetFields(flags))
            {
                SettingLabelAttribute? label = field.GetCustomAttribute<SettingLabelAttribute>();
                if (label != null)
                {
                    yield return label.LocalizationKey;
                }
            }
        }
    }

    /// <summary>
    /// У технических настроек графики диапазоны обязаны быть объявлены.
    /// </summary>
    /// <remarks>
    /// GraphicsQualitySettings — структура и живёт в ассете профиля, поэтому
    /// её границы объявлены юнитивским [Range]: инспектор ими ограничивает
    /// правку профиля, схема по ним проверяет, ползунок из них берёт края.
    /// Раньше те же восемь отрезков были записаны литералами ещё дважды — в
    /// GraphicsQualityProfile.ValidateSettings и в билдере вкладки графики.
    /// </remarks>
    private static void CheckGraphicsQualityRangesAreDeclared()
    {
        Section("у технических настроек графики объявлены диапазоны");
        foreach (FieldInfo field in typeof(GraphicsQualitySettings)
                     .GetFields(BindingFlags.Instance | BindingFlags.Public))
        {
            _checks++;
            bool unbounded = field.GetCustomAttribute<SettingUnboundedAttribute>() != null;
            try
            {
                SettingRangeAttribute range =
                    SettingSchema.RangeOf(typeof(GraphicsQualitySettings), field.Name);
                if (unbounded)
                {
                    Failures.Add($"GraphicsQualitySettings.{field.Name}: и диапазон, и [SettingUnbounded]");
                }
                else if (range.Minimum >= range.Maximum)
                {
                    Failures.Add(
                        $"GraphicsQualitySettings.{field.Name}: пустой диапазон " +
                        $"[{range.Minimum}, {range.Maximum}]");
                }
            }
            catch (InvalidOperationException) when (unbounded)
            {
                // Законно: перечисление режима освещения отрезком не описывается.
            }
            catch (InvalidOperationException ex)
            {
                Failures.Add($"GraphicsQualitySettings.{field.Name}: {ex.Message}");
            }
        }
    }

    private static Type[] SectionTypes()
    {
        return
        [
            typeof(AudioSettings),
            typeof(DisplaySettings),
            typeof(InterfaceSettings),
            typeof(AccessibilitySettings),
            typeof(ConnectionSettings),
            typeof(PostProcessSettings),
            typeof(WorldLightingSettings),
            typeof(TerrainSettings),
            typeof(EffectSettings),
        ];
    }

    /// <summary>
    /// Прогоняет действие по каждой секции конфига.
    /// </summary>
    /// <remarks>
    /// Обобщённые методы SettingSchema требуют типа на этапе компиляции,
    /// поэтому секции перечислены явно, а не выведены рефлексией: список
    /// в <see cref="SectionTypes"/> сверяется с ним отдельной проверкой ниже.
    /// </remarks>
    private static void ForEachSection(Action<string, Action, Func<bool>, Action> body)
    {
        Run<AudioSettings>();
        Run<DisplaySettings>();
        Run<InterfaceSettings>();
        Run<AccessibilitySettings>();
        Run<ConnectionSettings>();
        Run<PostProcessSettings>();
        Run<WorldLightingSettings>();
        Run<TerrainSettings>();
        Run<EffectSettings>();
        return;

        void Run<TSection>()
            where TSection : class, new()
        {
            body(
                typeof(TSection).Name,
                () => SettingSchema.Validate(new TSection()),
                () => SettingSchema.MatchesDefaults(new TSection()),
                () =>
                {
                    var section = new TSection();
                    PushExtremeValues(section);
                    SettingSchema.Clamp(section);
                    SettingSchema.Validate(section);
                });
        }
    }

    /// <summary>
    /// Загоняет в числовые поля заведомо запредельные значения.
    /// </summary>
    private static void PushExtremeValues<TSection>(TSection section)
        where TSection : class, new()
    {
        foreach (FieldInfo field in typeof(TSection).GetFields(BindingFlags.Instance | BindingFlags.Public))
        {
            if (field.GetCustomAttribute<SettingRangeAttribute>() == null)
            {
                continue;
            }

            if (field.FieldType == typeof(float))
            {
                field.SetValue(section, 1e6f);
            }
            else if (field.FieldType == typeof(int))
            {
                field.SetValue(section, int.MaxValue);
            }
            else if (field.FieldType == typeof(Vector2))
            {
                field.SetValue(section, new Vector2(-1e6f, 1e6f));
            }
        }
    }

    private static void CheckEveryFieldDeclaresConsumer()
    {
        Section("каждое поле настройки декларирует потребителя и механизм");
        var types = SectionTypes().Concat([typeof(GraphicsQualitySettings)]);
        foreach (Type type in types)
        {
            foreach (FieldInfo field in type.GetFields(BindingFlags.Instance | BindingFlags.Public))
            {
                _checks++;
                var consumer = field.GetCustomAttribute<SettingConsumerAttribute>();
                if (consumer == null)
                {
                    Failures.Add($"{type.Name}.{field.Name}: нет [SettingConsumer] — настройка не привязана к подсистеме");
                }
                else if (string.IsNullOrWhiteSpace(consumer.Mechanism))
                {
                    Failures.Add($"{type.Name}.{field.Name}: пустой механизм применения в [SettingConsumer]");
                }
            }
        }
    }

    private static void CheckLightingConfigHolderDirtyTracking()
    {
        Section("сеттеры освещения корректно детектируют изменения (dirty tracking)");
        var stub = new StubConfigManager();
        var holder = new Fodinae.World.Lighting.LightingConfigHolder(stub);

        TestSetter("DiffuseBounceEnabled", () => holder.SetDiffuseBounceEnabled(!holder.Lighting.DiffuseBounceEnabled), () => holder.SetDiffuseBounceEnabled(holder.Lighting.DiffuseBounceEnabled));
        TestSetter("FinalLightingClampEnabled", () => holder.SetFinalLightingClampEnabled(!holder.Lighting.EnableFinalLightingClamp), () => holder.SetFinalLightingClampEnabled(holder.Lighting.EnableFinalLightingClamp));
        TestSetter("AmbientIntensity", () => holder.SetAmbientIntensity(holder.Lighting.AmbientIntensity + 0.1f), () => holder.SetAmbientIntensity(holder.Lighting.AmbientIntensity));
        TestSetter("EmissionScale", () => holder.SetEmissionScale(holder.Lighting.EmissionScale > 4f ? holder.Lighting.EmissionScale - 1f : holder.Lighting.EmissionScale + 1f), () => holder.SetEmissionScale(holder.Lighting.EmissionScale));
        TestSetter("EmptyExtinctionMultiplier", () => holder.SetEmptyExtinctionMultiplier(holder.Lighting.EmptyExtinctionMultiplier + 0.2f), () => holder.SetEmptyExtinctionMultiplier(holder.Lighting.EmptyExtinctionMultiplier));
        TestSetter("SolidExtinctionMultiplier", () => holder.SetSolidExtinctionMultiplier(holder.Lighting.SolidExtinctionMultiplier > 1f ? holder.Lighting.SolidExtinctionMultiplier - 0.5f : holder.Lighting.SolidExtinctionMultiplier + 0.5f), () => holder.SetSolidExtinctionMultiplier(holder.Lighting.SolidExtinctionMultiplier));
        TestSetter("BounceStrength", () => holder.SetBounceStrength(holder.Lighting.BounceStrength > 0.5f ? 0.2f : 0.8f), () => holder.SetBounceStrength(holder.Lighting.BounceStrength));
        TestSetter("MaximumLightMultiplier", () => holder.SetMaximumLightMultiplier(holder.Lighting.MaximumLightMultiplier + 0.5f), () => holder.SetMaximumLightMultiplier(holder.Lighting.MaximumLightMultiplier));
        TestSetter("TransmittanceDebugDistance", () => holder.SetTransmittanceDebugDistance(holder.Lighting.TransmittanceDebugDistanceCells + 1f), () => holder.SetTransmittanceDebugDistance(holder.Lighting.TransmittanceDebugDistanceCells));
        TestSetter("MinimumTransmission", () => holder.SetMinimumTransmission(holder.Lighting.MinimumTransmission * 1.5f), () => holder.SetMinimumTransmission(holder.Lighting.MinimumTransmission));
        TestSetter("LightSafeBorder", () => holder.SetLightSafeBorder(holder.Lighting.LightSafeBorder + 1), () => holder.SetLightSafeBorder(holder.Lighting.LightSafeBorder));
        TestSetter("DynamicLightUpdatesPerSecond", () => holder.SetDynamicLightUpdatesPerSecond(holder.Lighting.DynamicLightUpdatesPerSecond + 5f), () => holder.SetDynamicLightUpdatesPerSecond(holder.Lighting.DynamicLightUpdatesPerSecond));
        TestSetter("AmbientColor", () => holder.SetAmbientColor(new Color(0.3f, 0.4f, 0.5f, 1f)), () => holder.SetAmbientColor(holder.Lighting.AmbientColor));
        TestSetter("EmptyExtinctionColor", () => holder.SetEmptyExtinctionColor(new Color(0.05f, 0.05f, 0.05f, 1f)), () => holder.SetEmptyExtinctionColor(holder.Lighting.EmptyExtinctionRgb));
        TestSetter("SolidExtinctionColor", () => holder.SetSolidExtinctionColor(new Color(0.8f, 0.8f, 0.8f, 1f)), () => holder.SetSolidExtinctionColor(holder.Lighting.SolidExtinctionRgb));
        TestSetter("DynamicLightSettings", () => holder.SetDynamicLightSettings(holder.Lighting.DynamicLightIntensity + 0.5f, Color.red), () => holder.SetDynamicLightSettings(holder.Lighting.DynamicLightIntensity, holder.Lighting.DynamicLightColor));

        void TestSetter(string name, Func<bool> change, Func<bool> repeat)
        {
            _checks++;
            if (!change())
            {
                Failures.Add($"LightingConfigHolder.{name}: не сообщил об изменении значения (вернул false)");
            }

            _checks++;
            if (repeat())
            {
                Failures.Add($"LightingConfigHolder.{name}: сообщил об изменении при повторном значении (вернул true)");
            }
        }
    }

    private static void CheckSettingMutationDetection()
    {
        Section("изменение каждого поля настройки сбрасывает статус авторских значений");
        TestSection<AudioSettings>();
        TestSection<DisplaySettings>();
        TestSection<InterfaceSettings>();
        TestSection<AccessibilitySettings>();
        TestSection<ConnectionSettings>();
        TestSection<PostProcessSettings>();
        TestSection<WorldLightingSettings>();
        TestSection<TerrainSettings>();
        TestSection<EffectSettings>();

        void TestSection<TSection>()
            where TSection : class, new()
        {
            foreach (FieldInfo field in typeof(TSection).GetFields(BindingFlags.Instance | BindingFlags.Public))
            {
                _checks++;
                var section = new TSection();
                MutateField(field, section);
                if (SettingSchema.MatchesDefaults(section))
                {
                    Failures.Add($"{typeof(TSection).Name}.{field.Name}: изменение поля не обнаружено в MatchesDefaults");
                }
            }
        }
    }

    private static void CheckGraphicsQualitySettingsMutationAndMsaaCycle()
    {
        Section("проверка мутации профилей качества графики и цикла смены MSAA");

        // Проверяем цикл переключения MSAA: 0 -> 2 -> 4 -> 8 -> 0
        int[] msaaCycle = [0, 2, 4, 8];
        int currentAa = 0;
        for (int i = 0; i < msaaCycle.Length; i++)
        {
            _checks++;
            int nextAa = currentAa switch
            {
                0 => 2,
                2 => 4,
                4 => 8,
                _ => 0,
            };

            int expected = msaaCycle[(i + 1) % msaaCycle.Length];
            if (nextAa != expected)
            {
                Failures.Add($"Цикл MSAA дал {nextAa} вместо ожидаемого {expected} при текущем {currentAa}");
            }

            _checks++;
            SettingRangeAttribute range = SettingSchema.RangeOf(typeof(GraphicsQualitySettings), nameof(GraphicsQualitySettings.AntiAliasing));
            if (nextAa < range.Minimum || nextAa > range.Maximum)
            {
                Failures.Add($"Значение MSAA {nextAa} выходит за объявленный диапазон [{range.Minimum}, {range.Maximum}]");
            }

            currentAa = nextAa;
        }

        // Проверяем, что смена AntiAliasing изменяет равенство и хэш GraphicsQualitySettings
        _checks++;
        var baseSettings = new GraphicsQualitySettings(2, 512, 128, 16, 20f, 1024, 1f, 0);
        var changedSettings = baseSettings;
        changedSettings.AntiAliasing = 4;
        if (baseSettings == changedSettings || baseSettings.GetHashCode() == changedSettings.GetHashCode())
        {
            Failures.Add("Смена AntiAliasing в GraphicsQualitySettings не изменила равенство структуры или GetHashCode");
        }
    }

    private static void CheckToneMappingWhitePointAndDisplayCalibration()
    {
        Section("калибровка белой точки тонмаппинга и HDR вывода");

        // 1. Константы и диапазоны белой точки тонмаппинга в PostProcessSettings
        _checks++;
        float wpMin = (float)typeof(PostProcessSettings).GetField(nameof(PostProcessSettings.ToneMappingWhitePointMin))!.GetValue(null)!;
        float wpMax = (float)typeof(PostProcessSettings).GetField(nameof(PostProcessSettings.ToneMappingWhitePointMax))!.GetValue(null)!;
        float wpDef = (float)typeof(PostProcessSettings).GetField(nameof(PostProcessSettings.DefaultToneMappingWhitePoint))!.GetValue(null)!;

        if (wpMin <= 0f)
        {
            Failures.Add("PostProcessSettings.ToneMappingWhitePointMin должен быть строго больше 0");
        }

        _checks++;
        if (wpMin >= wpMax)
        {
            Failures.Add("PostProcessSettings.ToneMappingWhitePointMin >= ToneMappingWhitePointMax");
        }

        _checks++;
        if (wpDef < wpMin || wpDef > wpMax)
        {
            Failures.Add("PostProcessSettings.DefaultToneMappingWhitePoint вне допустимого диапазона");
        }

        // 2. Сверка с PostProcessLook и PostProcessLimits через рефлексию
        _checks++;
        FieldInfo? lookWhitePoint = typeof(Fodinae.Rendering.PostProcessing.PostProcessLook.ColorGrading)
            .GetField("ToneMappingWhitePoint", BindingFlags.Public | BindingFlags.Static);
        if (lookWhitePoint == null)
        {
            Failures.Add("PostProcessLook.ColorGrading.ToneMappingWhitePoint не найден через рефлексию");
        }
        else
        {
            float lookVal = (float)lookWhitePoint.GetValue(null)!;
            if (lookVal != PostProcessSettings.DefaultToneMappingWhitePoint)
            {
                Failures.Add($"PostProcessLook.ColorGrading.ToneMappingWhitePoint ({lookVal}) не совпадает с DefaultToneMappingWhitePoint ({PostProcessSettings.DefaultToneMappingWhitePoint})");
            }
        }

        _checks++;
        FieldInfo? limitMin = typeof(Fodinae.Rendering.PostProcessing.PostProcessLimits)
            .GetField("ToneMappingWhitePointMin", BindingFlags.Public | BindingFlags.Static);
        FieldInfo? limitMax = typeof(Fodinae.Rendering.PostProcessing.PostProcessLimits)
            .GetField("ToneMappingWhitePointMax", BindingFlags.Public | BindingFlags.Static);
        if (limitMin == null || limitMax == null)
        {
            Failures.Add("PostProcessLimits для ToneMappingWhitePoint не найдены через рефлексию");
        }
        else
        {
            float minVal = (float)limitMin.GetValue(null)!;
            float maxVal = (float)limitMax.GetValue(null)!;
            if (minVal != PostProcessSettings.ToneMappingWhitePointMin || maxVal != PostProcessSettings.ToneMappingWhitePointMax)
            {
                Failures.Add($"Границы PostProcessLimits [{minVal}, {maxVal}] не совпадают с PostProcessSettings");
            }
        }

        // 3. Проверка клампинга и валидации через SettingSchema
        _checks++;
        var pp = new PostProcessSettings { ToneMappingWhitePoint = -5f };
        SettingSchema.Clamp(pp);
        if (pp.ToneMappingWhitePoint != PostProcessSettings.ToneMappingWhitePointMin)
        {
            Failures.Add($"SettingSchema.Clamp не ограничил белую точку снизу: {pp.ToneMappingWhitePoint}");
        }

        _checks++;
        pp.ToneMappingWhitePoint = 100f;
        SettingSchema.Clamp(pp);
        if (pp.ToneMappingWhitePoint != PostProcessSettings.ToneMappingWhitePointMax)
        {
            Failures.Add($"SettingSchema.Clamp не ограничил белую точку сверху: {pp.ToneMappingWhitePoint}");
        }

        // 4. Проверка математики HDR белой точки из DisplaySettings
        _checks++;
        float[] testPeaks = [DisplaySettings.PeakBrightnessMin, DisplaySettings.DefaultPeakBrightness, DisplaySettings.PeakBrightnessMax];
        float[] testPapers = [DisplaySettings.PaperWhiteMin, DisplaySettings.DefaultPaperWhite, DisplaySettings.PaperWhiteMax];
        foreach (float peak in testPeaks)
        {
            foreach (float paper in testPapers)
            {
                float hdrWhitePoint = Mathf.Max(0.5f, peak / Mathf.Max(10f, paper));
                if (float.IsNaN(hdrWhitePoint) || float.IsInfinity(hdrWhitePoint) || hdrWhitePoint < 0.5f)
                {
                    Failures.Add($"Недопустимое значение HDR белой точки ({hdrWhitePoint}) для peak={peak}, paper={paper}");
                }
            }
        }
    }

    private static void CheckPostProcessLookAllConstantsValidViaReflection()
    {
        Section("рефлексия всех констант и свойств PostProcessLook");
        Type lookType = typeof(Fodinae.Rendering.PostProcessing.PostProcessLook);
        foreach (Type nested in lookType.GetNestedTypes(BindingFlags.Public | BindingFlags.Static))
        {
            foreach (FieldInfo field in nested.GetFields(BindingFlags.Public | BindingFlags.Static))
            {
                _checks++;
                object? val = field.GetValue(null);
                if (val is float f && (float.IsNaN(f) || float.IsInfinity(f)))
                {
                    Failures.Add($"{nested.Name}.{field.Name}: невалидный float ({f}) в PostProcessLook");
                }
            }

            foreach (PropertyInfo prop in nested.GetProperties(BindingFlags.Public | BindingFlags.Static))
            {
                _checks++;
                object? val = prop.GetValue(null);
                if (val is Color c && (float.IsNaN(c.r) || float.IsNaN(c.g) || float.IsNaN(c.b) || float.IsNaN(c.a)))
                {
                    Failures.Add($"{nested.Name}.{prop.Name}: невалидный Color в PostProcessLook");
                }
                else if (val is Vector2 v && (float.IsNaN(v.x) || float.IsNaN(v.y)))
                {
                    Failures.Add($"{nested.Name}.{prop.Name}: невалидный Vector2 в PostProcessLook");
                }
            }
        }
    }

    private static void CheckConsumerTargetsAndMechanismsReflection(string root)
    {
        Section("глубокая проверка рефлексией потребителей настроек [SettingConsumer]");
        var types = SectionTypes().Concat([typeof(GraphicsQualitySettings)]);
        var csFilesCache = new Dictionary<string, string>();

        foreach (Type type in types)
        {
            foreach (FieldInfo field in type.GetFields(BindingFlags.Instance | BindingFlags.Public))
            {
                _checks++;
                var consumer = field.GetCustomAttribute<SettingConsumerAttribute>();
                if (consumer == null)
                {
                    continue;
                }

                string mechanism = consumer.Mechanism;
                var matches = Regex.Matches(
                    mechanism,
                    @"\b([A-Z][A-Za-z0-9_]+)\.([A-Za-z0-9_]+)\b");

                foreach (Match match in matches)
                {
                    string className = match.Groups[1].Value;
                    string memberName = match.Groups[2].Value;

                    if (className is "Screen" or "QualitySettings" or "Application" or "HDROutput" or "UniversalRenderPipelineAsset" or "Math" or "Mathf")
                    {
                        continue;
                    }

                    // 1. Проверяем среди типов текущей сборки (рефлексия)
                    Type? localType = AppDomain.CurrentDomain.GetAssemblies()
                        .SelectMany(a =>
                        {
                            try
                            {
                                return a.GetTypes();
                            }
                            catch
                            {
                                return Type.EmptyTypes;
                            }
                        })
                        .FirstOrDefault(t => t.Name == className);

                    if (localType != null)
                    {
                        MemberInfo[] members = localType.GetMember(
                            memberName,
                            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static);
                        if (members.Length == 0)
                        {
                            Failures.Add($"{type.Name}.{field.Name}: рефлексия не нашла член '{memberName}' в типе {className}");
                        }

                        continue;
                    }

                    // 2. Если тип вне сборки пробы, ищем исходник в Assets/Scripts
                    if (!csFilesCache.TryGetValue(className, out string? fileContent))
                    {
                        string[] found = Directory.GetFiles(
                            Path.Combine(root, "Assets/Scripts"),
                            $"{className}.cs",
                            SearchOption.AllDirectories);
                        if (found.Length > 0)
                        {
                            fileContent = File.ReadAllText(found[0]);
                            csFilesCache[className] = fileContent;
                        }
                        else
                        {
                            fileContent = null;
                            csFilesCache[className] = string.Empty;
                        }
                    }

                    if (string.IsNullOrEmpty(fileContent))
                    {
                        Failures.Add($"{type.Name}.{field.Name}: класс потребителя '{className}' не найден ни в сборке, ни в Assets/Scripts");
                    }
                    else if (!Regex.IsMatch(fileContent, $@"\b{memberName}\b"))
                    {
                        Failures.Add($"{type.Name}.{field.Name}: потребитель '{className}' не содержит члена '{memberName}'");
                    }
                }
            }
        }
    }

    private static void MutateField(FieldInfo field, object target)
    {
        if (field.FieldType == typeof(bool))
        {
            field.SetValue(target, !(bool)field.GetValue(target)!);
        }
        else if (field.FieldType == typeof(float))
        {
            float val = (float)field.GetValue(target)!;
            field.SetValue(target, val > 0.5f ? val - 0.2f : val + 0.2f);
        }
        else if (field.FieldType == typeof(int))
        {
            int val = (int)field.GetValue(target)!;
            field.SetValue(target, val + 1);
        }
        else if (field.FieldType == typeof(string))
        {
            field.SetValue(target, (string)field.GetValue(target)! + "_changed");
        }
        else if (field.FieldType == typeof(Color))
        {
            Color val = (Color)field.GetValue(target)!;
            field.SetValue(target, new Color(val.r + 0.1f, val.g, val.b, val.a));
        }
        else if (field.FieldType == typeof(Vector2))
        {
            Vector2 val = (Vector2)field.GetValue(target)!;
            field.SetValue(target, new Vector2(val.x + 1f, val.y));
        }
        else if (field.FieldType.IsEnum)
        {
            Array values = Enum.GetValues(field.FieldType);
            object current = field.GetValue(target)!;
            foreach (object v in values)
            {
                if (!Equals(v, current))
                {
                    field.SetValue(target, v);
                    break;
                }
            }
        }
    }

    private sealed class StubConfigManager : Fodinae.Core.Interfaces.IClientConfigManager
    {
        public ClientConfig Config { get; set; } = new() { Lighting = new WorldLightingSettings() };
        public string ConfigFilePath => "test_config.json";
        public GraphicsPreset SelectedGraphicsPreset => GraphicsPreset.Medium;
        public void EnsureInitialized() { }
        public void Load() { }
        public void Save() { }
        public void SaveDeferred() { }
        public void ApplyDefaults() { }
        public void MarkGraphicsAsCustom() { }
        public void SelectGraphicsPreset(GraphicsPreset preset) { }
        public void SetCustomGraphicsSettings(GraphicsQualitySettings settings) { }
        public void UpdateSection<TSection>(Func<ClientConfig, TSection> select, Action<TSection> update) where TSection : class, new() => update(select(Config));
        public void UpdateAndSave(Action<ClientConfig> update) => update(Config);
        public void UpdatePostProcessAndSave(Action<ClientConfig> update) => update(Config);
    }

    private static void Section(string title)
    {
        Console.WriteLine($"— {title}");
    }

    private static string ResolveRepositoryRoot(string[] args)
    {
        if (args.Length > 0)
        {
            return args[0];
        }

        var directory = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (directory != null && !Directory.Exists(Path.Combine(directory.FullName, "Assets")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? Directory.GetCurrentDirectory();
    }
}
