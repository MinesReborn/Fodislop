using Fodinae.Scripts;
using Fodinae.UI.Controls; // Add this using directive
using MinesServer.Networking.Server.Packets.GUI.Components;
using MinesServer.Networking.Server.Packets.GUI.Components.Input;
using UnityEngine.UIElements;

namespace Fodinae.Scripts.UI.Builders
{
    public class TextBoxPacketBuilder : PacketUIBuilderBase
    {
        public override VisualElement Build(IGUIComponentPacket packet, PacketUIBuilder builder)
        {
            if (packet is not TextBoxPacket textInputPkt)
            {
                return null;
            }

            var textField = new RegexTextField // Change to RegexTextField
            {
                value = textInputPkt.DefaultValue,
                isReadOnly = !textInputPkt.IsEnabled,
                Regex = textInputPkt.Regex, // Assign the Regex property
            };
            if (!string.IsNullOrEmpty(textInputPkt.Name))
            {
                textField.name = textInputPkt.Name;
            }

            return textField;
        }
    }
}
