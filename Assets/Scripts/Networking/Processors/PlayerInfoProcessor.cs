using Fodinae.Scripts.Core;
using Fodinae.Scripts.Core.Interfaces;
using Fodinae.Scripts.Game;
using Fodinae.Scripts.Game.Managers;
using Fodinae.Scripts.Player;
using Fodinae.Scripts.UI;
using Fodinae.Scripts.UI.HUD.Player.Model;
using MinesServer.Networking.Server.Packets.Information;
using MinesServer.Networking.Server.Packets.Movement;
using MinesServer.Networking.Server.Packets.World;
using UnityEngine;

namespace Fodinae.Scripts.Networking.Processors
{
    public class PlayerInfoProcessor : IPacketProcessor<PlayerInfoPacket>, IPacketProcessor<MovementSpeedPacket>, IPacketProcessor<TeleportPacket>
    {
        private static IMapDataProvider Map => ServiceLocator.Resolve<IMapDataProvider>() ?? MapManager.Instance;
        private static IPlayerStats Stats => ServiceLocator.Resolve<IPlayerStats>() ?? PlayerStatsModel.Instance;

        public void Process(PlayerInfoPacket packet)
        {
            var rm = RobotManager.Instance;
            if (rm != null)
            {
                rm.LocalPlayerBotId = packet.BotId;
            }

            var s = Stats;
            if (s != null)
            {
                s.SetNickname(packet.Nickname);
            }

            var player = PlayerMovementController.LocalPlayer;
            if (player != null)
            {
                if (player.TryGetComponent<Robot>(out var robot))
                {
                    robot.Initialize(packet.BotId);
                }

                player.Initialize(packet.BotId);
            }
        }

        public void Process(MovementSpeedPacket packet)
        {
            Map?.UpdateMovementSpeeds(packet);
        }

        public void Process(TeleportPacket packet)
        {
            var player = Object.FindAnyObjectByType<PlayerMovementController>();
            if (player == null || Map == null)
            {
                return;
            }

            player.transform.position = new Vector3(packet.X + 0.5f, packet.Y + 0.5f, 0);
            player.UpdateServerPosition(new Vector2Int(packet.X, packet.Y));
        }
    }
}
