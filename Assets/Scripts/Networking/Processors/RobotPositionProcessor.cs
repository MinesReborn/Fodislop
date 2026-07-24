using Fodinae.Scripts.Game.Managers;
using Fodinae.Scripts.Player;
using Fodinae.Scripts.Player.Logic;
using MinesServer.Networking.Server.Packets.World;
using UnityEngine;

namespace Fodinae.Scripts.Networking.Processors
{
    public class RobotPositionProcessor : IPacketProcessor<RobotPositionPacket>
    {
        public void Process(RobotPositionPacket packet)
        {
            var rm = RobotManager.Instance;
            if (rm == null)
            {
                return;
            }

            rm.UpdateRobotPosition(packet.BotId, packet.X, packet.Y, packet.Rotation);
            if (packet.BotId != 0 && packet.BotId == rm.LocalPlayerBotId)
            {
                var controller = PlayerMovementController.LocalPlayer;
                if (controller != null)
                {
                    controller.UpdateServerPosition(new Vector2Int(packet.X, packet.Y));
                }
            }
        }
    }
}
