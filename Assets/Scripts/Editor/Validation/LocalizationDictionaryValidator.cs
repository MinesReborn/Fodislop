#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Kern.Core.Localization;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Kern.Editor.Validation;

public sealed class LocalizationDictionaryValidator : IPreprocessBuildWithReport
{
    private const string LocalizationDirectory = "Assets/Resources/Localization";
    private const string ReferenceLanguage = "ru";
    private static readonly string[] s_languages = ["en", "ru", "zh", "zh-hant"];
    private static readonly Regex s_placeholderPattern = new(
        @"(?<!\{)\{(?<index>\d+)(?:,[^}:]+)?(?::[^}]*)?\}(?!\})",
        RegexOptions.CultureInvariant);

    public int callbackOrder => 0;

    [MenuItem("Kern/Localization/Validate Dictionaries")]
    public static void ValidateFromMenu()
    {
        IReadOnlyList<string> errors = CollectErrors();
        if (errors.Count > 0)
        {
            throw new InvalidOperationException(
                "Localization validation failed:" + Environment.NewLine +
                string.Join(Environment.NewLine, errors));
        }

        Debug.Log($"[Localization] Validated {s_languages.Length} language dictionaries.");
    }

    public void OnPreprocessBuild(BuildReport report)
    {
        IReadOnlyList<string> errors = CollectErrors();
        if (errors.Count > 0)
        {
            throw new BuildFailedException(
                "Localization validation failed:" + Environment.NewLine +
                string.Join(Environment.NewLine, errors));
        }
    }

    private static IReadOnlyList<string> CollectErrors()
    {
        List<string> errors = [];
        Dictionary<string, string>? reference = LoadDictionary(ReferenceLanguage, errors);
        if (reference == null)
        {
            return errors;
        }

        foreach (string language in s_languages)
        {
            if (language == ReferenceLanguage)
            {
                continue;
            }

            Dictionary<string, string>? localized = LoadDictionary(language, errors);
            if (localized == null)
            {
                continue;
            }

            foreach (string missingKey in reference.Keys.Except(localized.Keys, StringComparer.OrdinalIgnoreCase))
            {
                errors.Add($"{language}.json is missing key '{missingKey}'.");
            }

            foreach (string extraKey in localized.Keys.Except(reference.Keys, StringComparer.OrdinalIgnoreCase))
            {
                errors.Add($"{language}.json contains unknown key '{extraKey}'.");
            }

            foreach (KeyValuePair<string, string> entry in reference)
            {
                if (!localized.TryGetValue(entry.Key, out string? localizedValue))
                {
                    continue;
                }

                string[] expected = GetPlaceholderIndices(entry.Value);
                string[] actual = GetPlaceholderIndices(localizedValue);
                if (!expected.SequenceEqual(actual, StringComparer.Ordinal))
                {
                    errors.Add(
                        $"{language}.json key '{entry.Key}' has placeholders [{string.Join(", ", actual)}]; " +
                        $"expected [{string.Join(", ", expected)}].");
                }
            }
        }

        return errors;
    }

    private static Dictionary<string, string>? LoadDictionary(string language, ICollection<string> errors)
    {
        string path = Path.Combine(LocalizationDirectory, $"{language}.json");
        if (!File.Exists(path))
        {
            errors.Add($"Required localization file is missing: {path}.");
            return null;
        }

        try
        {
            return LocalizationDictionaryJson.Parse(File.ReadAllText(path));
        }
        catch (Exception exception)
        {
            errors.Add($"Could not parse {language}.json: {exception.Message}");
            return null;
        }
    }

    private static string[] GetPlaceholderIndices(string value) => s_placeholderPattern
        .Matches(value)
        .Select(match => match.Groups["index"].Value)
        .Distinct(StringComparer.Ordinal)
        .OrderBy(index => int.Parse(index, System.Globalization.CultureInfo.InvariantCulture))
        .ToArray();
}
