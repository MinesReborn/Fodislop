#nullable enable

using Fodinae.UI.Controls;
using MinesServer.Networking.Server.Packets.GUI.Components.Input;
using UnityEngine.UIElements;

namespace Fodinae.UI.Builders
{
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
            textField.AddToClassList("sci-fi-input");
            if (!string.IsNullOrEmpty(packet.Name))
            {
                textField.name = packet.Name;
            }

            return textField;
        }
    }
}
