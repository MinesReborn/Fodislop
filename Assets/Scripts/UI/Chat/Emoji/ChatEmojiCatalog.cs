#nullable enable

using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.TextCore.Text;

namespace Kern.UI.Chat;

// Каталог смайлов глобального чата и подключение emoji-шрифта.
//
// Рендер: в проектных SDF-лицах (Exo2/JetBrainsMono/Unbounded) emoji-глифов
// нет, а COLR-таблицы полноцветных шрифтов TextCore не растеризует. Смайлы
// рисуются монохромными глифами системного emoji-шрифта (Windows: Segoe UI
// Emoji), подключённого фолбэком к текстовым лицам — тот же механизм, каким
// Exo2 сегодня достаивает китайские глифы из NotoSans (m_FallbackFontAssetTable
// в Exo2_SDF.asset). Фолбэк срабатывает только для кодпоинтов, которых нет в
// основном шрифте, поэтому патч лиц не меняет остальной UI.
internal static class ChatEmojiCatalog
{
    // Стартовый набор: базовые реакции и горная тема (кирка, кристалл).
    public static readonly string[] Emojis =
    [
        "\U0001F600", // 😀 grin
        "\U0001F642", // 🙂 slight smile
        "\U0001F602", // 😂 tears of joy
        "\U0001F622", // 😢 cry
        "\U0001F44D", // 👍 thumbs up
        "❤",          // ❤ heart
        "⛏",          // ⛏ pick
        "\U0001F48E", // 💎 gem stone
    ];

    // Кандидаты системных emoji-шрифтов по платформам, пробуем по порядку.
    private static readonly string[] OsFontCandidates =
    [
        "Segoe UI Emoji",    // Windows
        "Apple Color Emoji", // macOS
        "Noto Color Emoji",  // Linux
    ];

    // Лица, в фолбэк которых добавляется emoji-шрифт. Сообщения и поле ввода
    // чата наследуют Exo2 (.unity-label в Theme.uss); моно и дисплей — на
    // случай смайлов в других контекстах.
    private static readonly string[] FacePaths =
    [
        "Fonts/Exo2/Exo2_SDF",
        "Fonts/JetBrainsMono/JetBrainsMono_SDF",
        "Fonts/Unbounded/Unbounded_SDF",
    ];

    private static FontAsset? s_emojiFont;
    private static bool s_installed;

    // Создаёт emoji-шрифт и дописывает его в фолбэк-таблицы лиц. Идемпотентно:
    // повторные вызовы возвращают уже созданный шрифт.
    public static FontAsset? EnsureInstalled()
    {
        if (s_installed)
        {
            return s_emojiFont;
        }

        s_installed = true;
        s_emojiFont = CreateEmojiFont();
        if (s_emojiFont == null)
        {
            Debug.LogWarning(
                "[ChatEmoji] Системный emoji-шрифт не найден — смайлы отрисуются квадратиками.");
            return null;
        }

        foreach (string facePath in FacePaths)
        {
            if (Resources.Load<FontAsset>(facePath) is not { } face)
            {
                continue;
            }

            face.fallbackFontAssetTable ??= new List<FontAsset>();
            if (!face.fallbackFontAssetTable.Contains(s_emojiFont))
            {
                face.fallbackFontAssetTable.Add(s_emojiFont);
            }
        }

        return s_emojiFont;
    }

    private static FontAsset? CreateEmojiFont()
    {
        string? family = FindOsEmojiFamily();
        if (family == null)
        {
            return null;
        }

        try
        {
            Font osFont = Font.CreateDynamicFontFromOSFont(family, 16);
            FontAsset? asset = FontAsset.CreateFontAsset(osFont);
            if (asset == null)
            {
                return null;
            }

            asset.name = "OS-Emoji-Fallback";
            asset.atlasPopulationMode = AtlasPopulationMode.Dynamic;

            // Предгрев глифов каталога: первый смайл не должен мигнуть
            // растеризацией. Неподдерживаемые кодпоинты просто пропустятся.
            asset.TryAddCharacters(string.Concat(Emojis), out _);
            return asset;
        }
        catch (Exception exception)
        {
            Debug.LogWarning($"[ChatEmoji] Не удалось создать шрифт из '{family}': {exception.Message}");
            return null;
        }
    }

    private static string? FindOsEmojiFamily()
    {
        string[] installed = Font.GetOSInstalledFontNames();
        foreach (string candidate in OsFontCandidates)
        {
            foreach (string name in installed)
            {
                if (string.Equals(name, candidate, StringComparison.OrdinalIgnoreCase))
                {
                    return name;
                }
            }
        }

        return null;
    }
}
