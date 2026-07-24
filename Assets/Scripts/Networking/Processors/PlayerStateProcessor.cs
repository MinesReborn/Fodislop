using Fodinae.Scripts.Player;
using Fodinae.Scripts.Player.Logic;
using MinesServer.Networking.Server.Packets.Information;

namespace Fodinae.Scripts.Networking.Processors
{
    public class PlayerStateProcessor : IPacketProcessor<AutoMineStatePacket>, IPacketProcessor<AggressionStatePacket>
    {
        public void Process(AutoMineStatePacket packet)
        {
            var player = PlayerMovementController.LocalPlayer;
            if (player != null)
            {
                player.AutoDig = packet.Enabled;
            }
        }

        public void Process(AggressionStatePacket packet)
        {
            var player = PlayerMovementController.LocalPlayer;
            if (player != null)
            {
                player.Aggression = packet.Enabled;
            }
        }
    }
}
