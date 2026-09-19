#nullable enable

using Kern.Core.Interfaces;
using MinesServer.Networking.Server.Packets.World;

namespace Kern.Networking.Processors;

public sealed class BuildingProcessor(IBuildingService buildingManager) :
    IPacketProcessor<PackPacket>,
    IPacketProcessor<RemovePackPacket>
{
    public void Process(PackPacket packet) =>
        buildingManager.AddOrUpdateBuilding(packet.X, packet.Y, packet.PackCode, packet.Variant, packet.LinkedClan);

    public void Process(RemovePackPacket packet) =>
        buildingManager.RemoveBuilding(packet.X, packet.Y);
}
