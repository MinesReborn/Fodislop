using Fodinae.Scripts.Core;
using Fodinae.Scripts.Core.Interfaces;
using Fodinae.Scripts.Game;
using Fodinae.Scripts.Game.Managers;
using Fodinae.Scripts.Player;
using Fodinae.Scripts.UI;
using Fodinae.Scripts.UI.HUD.Player.Model;
using MinesServer.Networking.Server.Packets.Information;

namespace Fodinae.Scripts.Networking.Processors
{
    public class ClanProcessor : IPacketProcessor<ShowClanPacket>, IPacketProcessor<HideClanPacket>
    {
        private static IPlayerStats Stats => ServiceLocator.Resolve<IPlayerStats>() ?? PlayerStatsModel.Instance;

        public void Process(ShowClanPacket packet)
        {
            Stats.SetClanId(packet.ClanId);
            var player = PlayerMovementController.LocalPlayer;
            if (player != null && player.TryGetComponent<Robot>(out var robot))
            {
                robot.SetClanBadge(packet.ClanId);
            }
        }

        public void Process(HideClanPacket packet)
        {
            var s = Stats;
            if (s != null)
            {
                s.SetClanId(0);
            }

            var player = PlayerMovementController.LocalPlayer;
            if (player != null && player.TryGetComponent<Robot>(out var robot))
            {
                robot.ClearClanBadge();
            }
        }
    }
}
