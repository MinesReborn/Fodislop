#nullable enable

using UnityEngine;

namespace Kern.Tools.Imgui;

// Утилиты для статичных GUI-стилей, которые используются в окнах
// грейдинга. Раньше каждый вызов брал стиль из ToolTheme, что добавляло
// dereference в горячем пути; здесь они вынесены в отдельный слой, чтобы
// не размазывать ответственность по стилям между двумя классами.
internal static class GuiStyles
{
    public static GUIStyle Button => ToolTheme.SecondaryButton;

    public static void WarningBanner(string text)
    {
        using (ToolLayout.Horizontal())
        {
            GUILayout.Label(text, ToolTheme.WarningLabel);
        }
    }

    public static void Spacing(float space)
    {
        if (space > 0f)
        {
            GUILayout.Space(space);
        }
    }
}
