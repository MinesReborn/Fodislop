using Fodinae.Scripts.Game.Managers;
using MinesServer.Networking.Server.Packets.World;

namespace Fodinae.Scripts.Networking.Processors
{
    public class MapRegionProcessor : IPacketProcessor<MapRegionPacket>
    {
        public void Process(MapRegionPacket packet)
        {
            var storage = MapStorage.Instance;
            if (storage?.CellLayer == null || packet.Payload == null)
            {
                return;
            }

            int index = 0;
            for (int y = 0; y <= packet.Height; y++)
            {
                for (int x = 0; x <= packet.Width; x++)
                {
                    if (index < packet.Payload.Length)
                    {
                        storage.SetCell(packet.X + x, packet.Y + y, packet.Payload[index++]);
                    }
                }
            }
        }
    }
}
