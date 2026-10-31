#nullable enable

using System;
using MinesServer.Networking.Server.Packets.GUI.Components;
using MinesServer.Networking.Server.Packets.GUI.Components.Visual;
using UnityEngine;
using UnityEngine.UIElements;

namespace Kern.UI.ServerWindows;
public class TextPacketBuilder : PacketUIBuilderBase<TextPacket>
{
    private const string ExitButtonLabel = "ВЫЙТИ";

    internal static bool IsExitButton(IGUIComponentPacket packet) =>
        packet is TextPacket text &&
        !string.IsNullOrEmpty(text.OnClickContext) &&
        string.Equals(text.Text.Trim(), ExitButtonLabel, StringComparison.Ordinal);

    protected override VisualElement BuildTyped(TextPacket packet, PacketUIBuilder builder)
    {
        if (!string.IsNullOrEmpty(packet.OnClickContext))
        {
            var button = new Button { text = packet.Text };
            button.AddToClassList("packet-window-action");
            // Кнопка канваса — тот же кликабельный текст, но с геометрией
            // (Canvas.X/Y/Width/Height задаёт сервер): фикс-минимум обычного
            // экшена сбрасывается, размер кнопки приходит из пакета.
            if (AttachedProperties.Has(packet, "Canvas.X"))
            {
                button.AddToClassList("packet-window-action--canvas");
            }

            return button;
        }

        var label = new Label(packet.Text);
        label.AddToClassList("sci-fi-text-body");
        label.AddToClassList("fit-wrap");
        // Некликабельный текст не ловит мышь: плашки уровней и подписи лежат
        // поверх кликабельных иконок канваса и не должны съедать их клики.
        label.pickingMode = PickingMode.Ignore;
        // Выравнивание в приёме legacy-клиента: текст по центру; префикс "@@"
        // (две собачки) — маркер автора страницы, такой текст прижат влево.
        bool leftAligned = packet.Text.StartsWith("@@", StringComparison.Ordinal);
        if (leftAligned)
        {
            label.text = packet.Text[2..];
            label.AddToClassList("packet-text--left");
        }
        else
        {
            label.AddToClassList("packet-text--center");
        }

        return label;
    }
}
