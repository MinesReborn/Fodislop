using Fodinae.Scripts.Core;
using Fodinae.Scripts.Core.Interfaces;
using MinesServer.Networking.Server.Packets.Mission;

namespace Fodinae.Scripts.Networking.Processors
{
    public class MissionProcessor : IPacketProcessor<MissionInitPacket>, IPacketProcessor<MissionProgressPacket>
    {
        private static IPlayerStats Stats => ServiceLocator.Resolve<IPlayerStats>();

        public void Process(MissionInitPacket packet)
        {
            if (string.IsNullOrEmpty(packet.Title))
            {
                Stats.ClearMission();
                return;
            }

            Stats.SetMission(packet.Title, packet.Description, 0);
        }

        public void Process(MissionProgressPacket packet)
        {
            var stats = Stats;
            stats.SetMissionProgress(packet.Current);
            if (packet.Max > 0)
            {
                stats.SetMissionMaxProgress(packet.Max);
            }
        }
    }
}
