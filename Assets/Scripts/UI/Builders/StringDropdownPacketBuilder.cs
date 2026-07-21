using System.Linq;
using Fodinae.Scripts;
using MinesServer.Networking.Server.Packets.GUI.Components;
using MinesServer.Networking.Server.Packets.GUI.Components.Input;
using UnityEngine.UIElements;

namespace Fodinae.Scripts.UI.Builders
{
    public class StringDropdownPacketBuilder : PacketUIBuilderBase
    {
        public override VisualElement Build(IGUIComponentPacket packet, PacketUIBuilder builder)
        {
            if (packet is not StringDropdownPacket strDropPkt)
            {
                return null;
            }

            var strOptions = strDropPkt.Values.ToList();
            var defaultValue = strDropPkt.DefaultValue;
            if (!strOptions.Contains(defaultValue))
            {
                defaultValue = strOptions.FirstOrDefault();
            }

            var strDrop = new DropdownField(strOptions, 0)
            {
                value = defaultValue,
            };
            strDrop.SetEnabled(strDropPkt.IsEnabled);
            return strDrop;
        }
    }
}
