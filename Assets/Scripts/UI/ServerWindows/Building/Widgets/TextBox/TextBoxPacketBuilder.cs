#nullable enable

using Kern.UI.Controls;
using MinesServer.Networking.Server.Packets.GUI.Components.Input;
using UnityEngine.UIElements;

namespace Kern.UI.ServerWindows;
public class TextBoxPacketBuilder : PacketUIBuilderBase<TextBoxPacket>
{
    protected override VisualElement BuildTyped(TextBoxPacket packet, PacketUIBuilder builder)
    {
        var textField = new RegexTextField
        {
            value = packet.DefaultValue,
            isReadOnly = !packet.IsEnabled,
            Regex = packet.Regex,
        };
        textField.SetEnabled(packet.IsEnabled);
        textField.AddToClassList("sci-fi-input");
        if (!string.IsNullOrEmpty(packet.Name))
        {
            textField.name = packet.Name;
        }

        return textField;
    }
}
