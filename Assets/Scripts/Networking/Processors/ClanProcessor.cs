#nullable enable

using Kern.Core.Interfaces;
using MinesServer.Networking.Server.Packets.Information;
using UnityEngine;

namespace Kern.Networking.Processors;

public sealed class ClanProcessor(IPlayerStats stats) :
    IPacketProcessor<ShowClanPacket>,
    IPacketProcessor<HideClanPacket>
{
    public void Process(ShowClanPacket packet) => stats.SetClanID(packet.ClanId);

    public void Process(HideClanPacket packet) => stats.SetClanID(0);
}
