#nullable enable

using System;
using Kern.Networking.Protocol;
using MinesServer.Networking.Server.Packets.GUI.Components;
using MinesServer.Networking.Server.Packets.GUI.Components.Visual;
using UnityEngine;
using UnityEngine.UIElements;

namespace Kern.UI.ServerWindows;
public class TextPacketBuilder : PacketUIBuilderBase<TextPacket>
{
    internal static bool IsCloseAction(IGUIComponentPacket packet) =>
        string.Equals(
            AttachedProperties.Find(packet, ServerWindowActionMetadata.ActionKey),
            ServerWindowActionMetadata.CloseActionValue,
            StringComparison.Ordinal);

    protected override VisualElement BuildTyped(TextPacket packet, PacketUIBuilder builder)
    {
        if (!string.IsNullOrEmpty(packet.OnClickContext))
        {
            var button = new Button { text = packet.Text };
            button.AddToClassList("packet-window-action");
            // Кнопка канваса — тот же кликабельный текст, но с геометрией
            // (Canvas.X/Y/Width/Height задаёт сервер): фикс-минимум обычного
            // экшена сбрасывается, размер кнопки приходит из пакета.
            if (AttachedProperties.Has(packet, "Canvas.X") ||
                AttachedProperties.Has(packet, "Canvas.Y") ||
                AttachedProperties.Has(packet, "Canvas.Width") ||
                AttachedProperties.Has(packet, "Canvas.Height"))
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
        label.AddToClassList("packet-text--center");

        return label;
    }
}
