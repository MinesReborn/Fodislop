#nullable enable

using Kern.Core.Interfaces;
using MinesServer.Networking.Server.Packets.Mission;

namespace Kern.Networking.Processors;

public sealed class MissionProcessor(IPlayerStats playerStats) :
    IPacketProcessor<MissionInitPacket>,
    IPacketProcessor<MissionProgressPacket>
{
    public void Process(MissionInitPacket packet)
    {
        if (string.IsNullOrEmpty(packet.Title))
        {
            playerStats.ClearMission();
            return;
        }

        playerStats.SetMission(packet.Title, packet.Description, 0);
    }

    public void Process(MissionProgressPacket packet)
    {
        playerStats.SetMissionProgress(packet.Current);
        if (packet.Max > 0)
        {
            playerStats.SetMissionMaxProgress(packet.Max);
        }
    }
}
