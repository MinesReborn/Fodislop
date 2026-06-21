using Fodinae.Scripts;
using MinesServer.Networking.Server.Packets.GUI;
using MinesServer.Networking.Server.Packets.GUI.Components;
using UnityEngine.UIElements;

namespace Fodinae.Scripts.UI.Builders
{
    public abstract class PacketUIBuilderBase
    {
        public abstract VisualElement Build(IGUIComponentPacket packet, PacketUIBuilder builder);
    }
}
