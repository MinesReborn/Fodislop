using Fodinae.Scripts.Game.Managers;
using MinesServer.Networking.Server.Packets.World;

namespace Fodinae.Scripts.Networking.Processors
{
    public class PackProcessor : IPacketProcessor<PackPacket>, IPacketProcessor<RemovePackPacket>
    {
        public void Process(PackPacket packet)
        {
            PackManager.Instance?.AddOrUpdatePack(packet.X, packet.Y, packet.PackCode, packet.Variant, packet.LinkedClan);
        }

        public void Process(RemovePackPacket packet)
        {
            PackManager.Instance?.RemovePack(packet.X, packet.Y);
        }
    }
}
