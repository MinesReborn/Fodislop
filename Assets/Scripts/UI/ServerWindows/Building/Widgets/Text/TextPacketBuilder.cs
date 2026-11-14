#nullable enable

using System;
using Kern.Networking.Protocol;
using MinesServer.Networking.Server.Packets.GUI;
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
            if (AttachedProperties.Has(packet, AttachedPropertyKeys.CanvasX) ||
                AttachedProperties.Has(packet, AttachedPropertyKeys.CanvasY) ||
                AttachedProperties.Has(packet, AttachedPropertyKeys.CanvasWidth) ||
                AttachedProperties.Has(packet, AttachedPropertyKeys.CanvasHeight))
            {
                button.AddToClassList("packet-window-action--canvas");
            }

            ApplyFontClass(button, packet);
            return button;
        }

        var label = new Label(packet.Text);
        label.AddToClassList("sci-fi-text-body");
        label.AddToClassList("fit-wrap");
        // Некликабельный текст не ловит мышь: плашки уровней и подписи лежат
        // поверх кликабельных иконок канваса и не должны съедать их клики.
        label.pickingMode = PickingMode.Ignore;
        label.AddToClassList("packet-text--center");
        ApplyFontClass(label, packet);

        return label;
    }

    // Гарнитура серверного текста (AttachedPropertyKeys.TextFont): имя из
    // пакета становится скин-классом packet-font-*, правило PacketWindowKern.uss
    // выбирает FontAsset темы. Rich-text <font> парсер UITK не поддерживает,
    // поэтому гарнитура передаётся только этим attached-свойством.
    private static void ApplyFontClass(VisualElement element, TextPacket packet)
    {
        string font = AttachedProperties.Find(packet, AttachedPropertyKeys.TextFont);
        if (!string.IsNullOrEmpty(font))
        {
            element.AddToClassList($"packet-font-{font.ToLowerInvariant()}");
        }
    }
}
