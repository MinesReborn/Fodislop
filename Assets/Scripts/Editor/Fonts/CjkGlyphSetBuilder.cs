#nullable enable

using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Kern.Editor;

internal static class CjkGlyphSetBuilder
{
    public const string EnglishLocalizationPath = "Assets/Resources/Localization/en.json";
    public const string RussianLocalizationPath = "Assets/Resources/Localization/ru.json";
    public const string SimplifiedLocalizationPath = "Assets/Resources/Localization/zh.json";
    public const string TraditionalLocalizationPath = "Assets/Resources/Localization/zh-hant.json";

    private const string RussianAlphabet =
        "АБВГДЕЁЖЗИЙКЛМНОПРСТУФХЦЧШЩЪЫЬЭЮЯ" +
        "абвгдеёжзийклмнопрстуфхцчшщъыьэюя";

    public static string BuildCharacterSet(bool includeCjk)
    {
        var characters = new SortedSet<char>();
        AddRange(characters, 0x20, 0x7E);
        AddFileCharacters(characters, EnglishLocalizationPath);
        AddFileCharacters(characters, RussianLocalizationPath);
        if (includeCjk)
        {
            AddFileCharacters(characters, SimplifiedLocalizationPath);
            AddFileCharacters(characters, TraditionalLocalizationPath);
        }

        foreach (char character in RussianAlphabet)
        {
            characters.Add(character);
        }

        var result = new StringBuilder(characters.Count);
        foreach (char character in characters)
        {
            result.Append(character);
        }

        return result.ToString();
    }

    public static string FormatCodePoints(string characters)
    {
        if (string.IsNullOrEmpty(characters))
        {
            return "unknown";
        }

        var codePoints = new string[characters.Length];
        for (int i = 0; i < characters.Length; i++)
        {
            codePoints[i] = $"U+{(int)characters[i]:X4}";
        }

        return string.Join(", ", codePoints);
    }

    private static void AddFileCharacters(ISet<char> characters, string path)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException("Required localization file is missing.", path);
        }

        foreach (char character in File.ReadAllText(path))
        {
            if (!char.IsControl(character))
            {
                characters.Add(character);
            }
        }
    }

    private static void AddRange(ISet<char> characters, int first, int last)
    {
        for (int codePoint = first; codePoint <= last; codePoint++)
        {
            characters.Add((char)codePoint);
        }
    }
}
