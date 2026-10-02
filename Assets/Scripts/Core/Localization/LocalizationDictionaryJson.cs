#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;

namespace Kern.Core.Localization;

internal static class LocalizationDictionaryJson
{
    public static Dictionary<string, string> Parse(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            throw new ArgumentException("Localization JSON must not be empty.", nameof(json));
        }

        Dictionary<string, string?>? parsed = JsonConvert.DeserializeObject<Dictionary<string, string?>>(json,
            new JsonSerializerSettings
            {
                CheckAdditionalContent = true,
            });
        if (parsed == null)
        {
            throw new InvalidDataException("Localization JSON must contain an object of string values.");
        }

        Dictionary<string, string> result = new(StringComparer.OrdinalIgnoreCase);
        foreach (KeyValuePair<string, string?> entry in parsed)
        {
            if (string.IsNullOrWhiteSpace(entry.Key))
            {
                throw new InvalidDataException("Localization keys must not be empty.");
            }

            if (entry.Value == null)
            {
                throw new InvalidDataException($"Localization value for '{entry.Key}' must not be null.");
            }

            if (!result.TryAdd(entry.Key, entry.Value))
            {
                throw new InvalidDataException(
                    $"Localization contains duplicate keys that differ only by case: '{entry.Key}'.");
            }
        }

        return result;
    }
}
