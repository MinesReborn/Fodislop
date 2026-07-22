using Fodinae.Scripts.Core;
using Fodinae.Scripts.Core.Interfaces;
using MinesServer.Networking.Server.Packets.Connection;
using UnityEngine;

namespace Fodinae.Scripts.Networking.Processors
{
    public class WorldInitProcessor : IPacketProcessor<WorldInitPacket>
    {
        public void Process(WorldInitPacket packet)
        {
            Debug.Log($"[WorldInitProcessor] Processing WorldInit: width={packet.Width}, height={packet.Height}");

            var map = ServiceLocator.Resolve<IMapDataProvider>();
            map?.LoadWorldInit(packet);
        }
    }
}
