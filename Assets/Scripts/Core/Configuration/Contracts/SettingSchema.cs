#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace Kern.Core;

public static class SettingSchema
{
    public readonly record struct SettingField(
        FieldInfo Field,
        SettingRangeAttribute? Range,
        SettingLabelAttribute? Label)
    {
        public string Name => Field.Name;
    }

    private static readonly Dictionary<Type, SettingField[]> _FieldCache = [];

    private static class Cache<TSection>
        where TSection : class, new()
    {
        public static readonly TSection Defaults = new();
    }

    public static IReadOnlyList<SettingField> Describe(Type sectionType)
    {
        if (sectionType == null)
        {
            throw new ArgumentNullException(nameof(sectionType));
        }

        lock (_FieldCache)
        {
            if (_FieldCache.TryGetValue(sectionType, out SettingField[]? cached))
            {
                return cached;
            }

            SettingField[] fields = sectionType
                .GetFields(BindingFlags.Instance | BindingFlags.Public)
                .Where(field => !field.IsInitOnly)
                .Select(field => new SettingField(
                    field,
                    ResolveRange(field),
                    field.GetCustomAttribute<SettingLabelAttribute>()))
                .ToArray();
            _FieldCache[sectionType] = fields;
            return fields;
        }
    }

    public static IReadOnlyList<SettingField> Describe<TSection>()
        where TSection : class, new()
    {
        return Describe(typeof(TSection));
    }

    private static SettingRangeAttribute? ResolveRange(FieldInfo field)
    {
        SettingRangeAttribute? own = field.GetCustomAttribute<SettingRangeAttribute>();
        if (own != null)
        {
            return own;
        }

        RangeAttribute? unity = field.GetCustomAttribute<RangeAttribute>();
        return unity == null ? null : new SettingRangeAttribute(unity.min, unity.max);
    }

    public static SettingRangeAttribute RangeOf<TSection>(string fieldName)
        where TSection : class, new()
    {
        return RangeOf(typeof(TSection), fieldName);
    }

    /// <inheritdoc cref="RangeOf{TSection}(string)"/>
    public static SettingRangeAttribute RangeOf(Type sectionType, string fieldName)
    {
        foreach (SettingField field in Describe(sectionType))
        {
            if (string.Equals(field.Name, fieldName, StringComparison.Ordinal))
            {
                return field.Range ?? throw new InvalidOperationException(
                    $"{sectionType.Name}.{fieldName} has no declared range; " +
                    "a slider cannot be built from it.");
            }
        }

        throw new InvalidOperationException(
            $"{sectionType.Name} has no public field '{fieldName}'.");
    }

    public static string LabelOf<TEnum>(TEnum value)
        where TEnum : struct, Enum
    {
        string name = value.ToString();
        FieldInfo member = typeof(TEnum).GetField(name) ?? throw new InvalidOperationException(
            $"{typeof(TEnum).Name} has no member '{name}'.");
        return member.GetCustomAttribute<SettingLabelAttribute>()?.LocalizationKey
            ?? throw new InvalidOperationException(
                $"{typeof(TEnum).Name}.{name} has no [SettingLabel]; it cannot be shown to the player.");
    }

    public static string LabelOf<TSection>(string fieldName)
        where TSection : class, new()
    {
        return LabelOf(typeof(TSection), fieldName);
    }

    /// <inheritdoc cref="LabelOf{TSection}(string)"/>
    public static string LabelOf(Type sectionType, string fieldName)
    {
        foreach (SettingField field in Describe(sectionType))
        {
            if (string.Equals(field.Name, fieldName, StringComparison.Ordinal))
            {
                return field.Label?.LocalizationKey ?? throw new InvalidOperationException(
                    $"{sectionType.Name}.{fieldName} has no [SettingLabel]; " +
                    "it is not meant to be shown to the player.");
            }
        }

        throw new InvalidOperationException(
            $"{sectionType.Name} has no public field '{fieldName}'.");
    }

    public static void Validate<TSection>(TSection section)
        where TSection : class, new()
    {
        if (section == null)
        {
            throw new InvalidDataException($"{typeof(TSection).Name} section is missing.");
        }

        Validate(section, typeof(TSection));
    }

    /// <inheritdoc cref="Validate{TSection}(TSection)"/>
    public static void Validate(object section, Type sectionType)
    {
        if (section == null)
        {
            throw new ArgumentNullException(nameof(section));
        }

        if (sectionType == null)
        {
            throw new ArgumentNullException(nameof(sectionType));
        }

        foreach (SettingField field in Describe(sectionType))
        {
            object? value = field.Field.GetValue(section);
            string name = $"{sectionType.Name}.{field.Name}";
            switch (value)
            {
                case float number:
                    RequireInRange(number, field.Range, name);
                    break;
                case int number:
                    // Без условия на наличие диапазона: целое поле без
                    // [SettingRange] (разрешение экрана, режим окна, предел
                    // кадров) законно, проверять у него нечего, и оно обязано
                    // спокойно пройти дальше. С условием оно не совпадало ни с
                    // одной веткой и падало в default ниже — ровно та
                    // «настройка умирает молча», от которой этот класс и
                    // защищает, только наоборот: громко и на ровном месте.
                    RequireInRange(number, field.Range, name);
                    break;
                case Color color:
                    // У цвета диапазон не объявляется: компоненты бывают выше
                    // единицы (SolidExtinctionRGB авторски равен 1.2). Требуется
                    // только конечность и неотрицательность — отрицательная
                    // яркость физически невозможна и ломает решатель света.
                    RequireFiniteNonNegative(color.r, $"{name}.r");
                    RequireFiniteNonNegative(color.g, $"{name}.g");
                    RequireFiniteNonNegative(color.b, $"{name}.b");
                    RequireFiniteNonNegative(color.a, $"{name}.a");
                    break;
                case Vector2 vector:
                    RequireInRange(vector.x, field.Range, $"{name}.x");
                    RequireInRange(vector.y, field.Range, $"{name}.y");
                    break;
                case bool:
                case string:
                case Enum:
                    // Проверять нечем: у них нет отрезка по существу.
                    // Перечень строковых значений (язык, адрес сервера)
                    // проверяется отдельно в ClientConfigValidator.
                    break;
                default:
                    // Поле типа, о котором эта схема не знает, молча проходило
                    // бы любую проверку — включая объявленный над ним
                    // [SettingRange], который выглядел бы действующим.
                    throw new InvalidDataException(
                        $"Setting '{name}' has type {field.Field.FieldType.Name}, which " +
                        "SettingSchema cannot validate. Add a case for it here, or the " +
                        "declared range silently does nothing.");
            }
        }
    }

    public static void Clamp<TSection>(TSection section)
        where TSection : class, new()
    {
        foreach (SettingField field in Describe(typeof(TSection)))
        {
            if (field.Range == null)
            {
                continue;
            }

            object? value = field.Field.GetValue(section);
            switch (value)
            {
                case float number:
                    field.Field.SetValue(
                        section,
                        Mathf.Clamp(number, field.Range.Minimum, field.Range.Maximum));
                    break;
                case int number:
                    field.Field.SetValue(
                        section,
                        Mathf.Clamp(number, (int)field.Range.Minimum, (int)field.Range.Maximum));
                    break;
                case Vector2 vector:
                    field.Field.SetValue(
                        section,
                        new Vector2(
                            Mathf.Clamp(vector.x, field.Range.Minimum, field.Range.Maximum),
                            Mathf.Clamp(vector.y, field.Range.Minimum, field.Range.Maximum)));
                    break;
                default:
                    // Сюда попадают только поля с объявленным диапазоном:
                    // ветка выше отсеивает те, у которых его нет. Значит тип,
                    // до которого клампинг не дотягивается, — это диапазон,
                    // который ничего не ограничивает.
                    throw new InvalidDataException(
                        $"Setting '{typeof(TSection).Name}.{field.Name}' declares a range " +
                        $"on type {field.Field.FieldType.Name}, which SettingSchema cannot clamp.");
            }
        }
    }

    public static bool MatchesDefaults<TSection>(TSection? section)
        where TSection : class, new()
    {
        if (section == null)
        {
            return false;
        }

        TSection defaults = Cache<TSection>.Defaults;
        foreach (SettingField field in Describe(typeof(TSection)))
        {
            if (!Equals(field.Field.GetValue(section), field.Field.GetValue(defaults)))
            {
                return false;
            }
        }

        return true;
    }

    private static void RequireInRange(float value, SettingRangeAttribute? range, string name)
    {
        if (float.IsNaN(value) || float.IsInfinity(value))
        {
            throw new InvalidDataException($"Setting '{name}' must be finite.");
        }

        if (range != null && (value < range.Minimum || value > range.Maximum))
        {
            throw new InvalidDataException(
                $"Setting '{name}' is {value}, outside [{range.Minimum}, {range.Maximum}].");
        }
    }

    private static void RequireFiniteNonNegative(float value, string name)
    {
        if (float.IsNaN(value) || float.IsInfinity(value) || value < 0f)
        {
            throw new InvalidDataException(
                $"Setting '{name}' must be finite and non-negative.");
        }
    }
}
