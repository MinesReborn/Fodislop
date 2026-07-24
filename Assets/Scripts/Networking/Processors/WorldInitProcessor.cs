using Fodinae.Scripts.Game.Managers;
using MinesServer.Networking.Server.Packets.Connection;

namespace Fodinae.Scripts.Networking.Processors
{
    public class WorldInitProcessor : IPacketProcessor<WorldInitPacket>
    {
        public void Process(WorldInitPacket packet)
        {
            MapManager.Instance.LoadWorldInit(packet);
        }
    }
}
