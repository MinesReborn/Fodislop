#nullable enable

using Kern.Core.Interfaces;
using MinesServer.Networking.Server.Packets.Mission;

namespace Kern.Networking.Processors;

public sealed class MissionArrowProcessor(IPlayerStats playerStats) : IPacketProcessor<MissionArrowPacket>
{
    public void Process(MissionArrowPacket packet) =>
        playerStats.SetMissionArrow(packet.X, packet.Y);
}
