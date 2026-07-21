using System.Linq;
using Fodinae.Scripts;
using MinesServer.Networking.Server.Packets.GUI.Components;
using MinesServer.Networking.Server.Packets.GUI.Components.Input;
using UnityEngine.UIElements;

namespace Fodinae.Scripts.UI.Builders
{
    public class IntDropdownPacketBuilder : PacketUIBuilderBase
    {
        public override VisualElement Build(IGUIComponentPacket packet, PacketUIBuilder builder)
        {
            if (packet is not IntDropdownPacket intDropPkt)
            {
                return null;
            }

            var intOptions = intDropPkt.Values.Select(x => x.ToString()).ToList();
            var defaultValue = intDropPkt.DefaultValue.ToString();
            if (!intOptions.Contains(defaultValue))
            {
                defaultValue = intOptions.FirstOrDefault();
            }

            var intDrop = new DropdownField(intOptions, 0)
            {
                value = defaultValue,
            };
            intDrop.SetEnabled(intDropPkt.IsEnabled);
            return intDrop;
        }
    }
}
