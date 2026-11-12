#nullable enable

using MinesServer.Data;
using MinesServer.Networking.Server.Packets.GUI.Components.Visual;
using UnityEngine;
using UnityEngine.UIElements;

namespace Kern.UI.ServerWindows;

/// <summary>
/// Стиль текста, назначенный сервером в TextPacket. Поля с нулевыми
/// значениями («наследование») не трогаются: нулевая альфа цвета, кегль 0,
/// Text и Default оставляют решение скину окна.
/// </summary>
public static class TextStyler
{
    public static void Apply(VisualElement element, TextPacket packet)
    {
        if (packet.TextColor.A > 0)
        {
            // StyleColor не умеет неявно принимать Color32 — только Color.
            element.style.color = (Color)new Color32(packet.TextColor.R, packet.TextColor.G, packet.TextColor.B, packet.TextColor.A);
        }

        if (packet.FontSize > 0)
        {
            element.style.fontSize = packet.FontSize;
        }

        switch (packet.FontId)
        {
            case FontId.Display:
                element.AddToClassList("packet-font-display");
                break;
            case FontId.Mono:
                element.AddToClassList("packet-font-mono");
                break;
        }

        if (packet.IsBold)
        {
            element.style.unityFontStyleAndWeight = FontStyle.Bold;
        }

        if (packet.LetterSpacing > 0)
        {
            element.style.letterSpacing = packet.LetterSpacing;
        }

        switch (packet.TextAlign)
        {
            case TextAlign.Left:
                element.style.unityTextAlign = TextAnchor.MiddleLeft;
                break;
            case TextAlign.Center:
                element.style.unityTextAlign = TextAnchor.MiddleCenter;
                break;
            case TextAlign.Right:
                element.style.unityTextAlign = TextAnchor.MiddleRight;
                break;
        }
    }
}
