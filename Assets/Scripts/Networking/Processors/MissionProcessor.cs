using Fodinae.Scripts.Core;
using Fodinae.Scripts.Core.Interfaces;
using Fodinae.Scripts.UI;
using Fodinae.Scripts.UI.HUD.Player.Model;
using MinesServer.Networking.Server.Packets.Mission;

namespace Fodinae.Scripts.Networking.Processors
{
    public class MissionProcessor : IPacketProcessor<MissionInitPacket>, IPacketProcessor<MissionProgressPacket>
    {
        private static IPlayerStats Stats => ServiceLocator.Resolve<IPlayerStats>() ?? PlayerStatsModel.Instance;

        public void Process(MissionInitPacket packet)
        {
            var s = Stats;
            if (s == null)
            {
                return;
            }

            if (string.IsNullOrEmpty(packet.Title))
            {
                s.ClearMission();
                return;
            }

            s.SetMission(packet.Title, packet.Description, 0);
        }

        public void Process(MissionProgressPacket packet)
        {
            var s = Stats;
            if (s == null)
            {
                return;
            }

            s.SetMissionProgress(packet.Current);
            if (packet.Max > 0)
            {
                s.SetMissionMaxProgress(packet.Max);
            }
        }
    }
}
